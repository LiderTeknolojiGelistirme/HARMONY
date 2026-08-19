using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using UnityEngine.XR.Interaction.Toolkit.UI;

/// <summary>
/// Canlı çoklu seri çizgi grafiği — web arayüzündeki Chart.js panellerinin
/// (Joint Positions / Joint Velocities / Cleaning Force) AR karşılığı.
///
/// Unity'nin yerleşik UI sisteminde çizgi grafiği bileşeni yoktur; bu sınıf
/// <see cref="MaskableGraphic"/>'ten türeyip <see cref="OnPopulateMesh"/>
/// içinde ızgarayı ve serileri doğrudan mesh olarak üretir. Böylece harici
/// bir grafik paketi gerekmez ve tek draw call ile çizilir.
///
/// Zaman ekseni kayan pencere şeklindedir: sağ kenar "şimdi", sol kenar
/// "şimdi - <see cref="windowSeconds"/>".
///
/// Kumanda ışını (veya fare) grafiğin üzerine geldiğinde imlecin bulunduğu
/// x konumunda dikey bir kılavuz çizgi ile her serinin o andaki noktası
/// işaretlenir; sayısal değerler <see cref="HoverReadings"/> üzerinden
/// okunup <see cref="HarmonyChartTooltip"/> tarafından gösterilir. Web
/// arayüzündeki Chart.js tooltip'inin (mode: nearest, axis: x) karşılığıdır.
/// </summary>
[RequireComponent(typeof(CanvasRenderer))]
[RequireComponent(typeof(RectTransform))]
public class HarmonyLineChart : MaskableGraphic,
    IPointerEnterHandler, IPointerMoveHandler, IPointerExitHandler
{
    /// <summary>İmlecin bulunduğu andaki tek bir seri okuması.</summary>
    public readonly struct HoverReading
    {
        /// <summary>Serinin grafikteki sırası.</summary>
        public readonly int SeriesIndex;

        /// <summary>Serinin açıklama etiketi.</summary>
        public readonly string Label;

        /// <summary>Serinin çizgi rengi.</summary>
        public readonly Color Color;

        /// <summary>İmlece en yakın örneğin değeri.</summary>
        public readonly float Value;

        /// <summary>Okumanın alındığı örneğin zaman damgası (<see cref="Time.time"/> ölçeği).</summary>
        public readonly float SampleTime;

        /// <summary>
        /// Yeni bir imleç okuması oluşturur.
        /// </summary>
        /// <param name="seriesIndex">Serinin grafikteki sırası.</param>
        /// <param name="label">Seri etiketi.</param>
        /// <param name="color">Seri rengi.</param>
        /// <param name="value">Okunan değer.</param>
        /// <param name="sampleTime">Örneğin zaman damgası.</param>
        public HoverReading(int seriesIndex, string label, Color color, float value, float sampleTime)
        {
            SeriesIndex = seriesIndex;
            Label = label;
            Color = color;
            Value = value;
            SampleTime = sampleTime;
        }
    }

    /// <summary>Grafikte çizilen tek bir veri serisi.</summary>
    [System.Serializable]
    public class Series
    {
        [Tooltip("Açıklama etiketinde görünecek ad.")]
        public string label = "series";

        [Tooltip("Çizgi rengi.")]
        public Color color = new Color32(0x2d, 0xd4, 0xbf, 255);

        /// <summary>(zaman, değer) örnekleri. Zaman <see cref="Time.time"/> ölçeğindedir.</summary>
        [System.NonSerialized] public readonly List<Vector2> Points = new List<Vector2>();
    }

    [Header("Seriler")]
    [SerializeField] private List<Series> series = new List<Series>();

    [Header("Zaman Ekseni")]
    [Tooltip("Grafikte gösterilen pencere genişliği [s].")]
    [SerializeField] private float windowSeconds = 20f;

    [Header("Değer Ekseni")]
    [Tooltip("Y ekseni alt sınırı (otomatik ölçek kapalıyken).")]
    [SerializeField] private float yMin = -2f;

    [Tooltip("Y ekseni üst sınırı (otomatik ölçek kapalıyken).")]
    [SerializeField] private float yMax = 1f;

    [Tooltip("Y aralığını veriye göre kendiliğinden ayarla.")]
    [SerializeField] private bool autoScaleY = true;

    [Tooltip("Otomatik ölçekte üstte/altta bırakılacak pay (oran).")]
    [SerializeField] private float autoScalePadding = 0.15f;

    [Tooltip("Otomatik ölçekte eksenin alabileceği en dar aralık. " +
             "Veri neredeyse sabitken eksenin titremesini önler.")]
    [SerializeField] private float minAutoRange = 0.2f;

    [Tooltip("Eksen sınırlarının yeni değerlere yaklaşma hızı [1/s]. " +
             "Yüksek değer daha çabuk, düşük değer daha yumuşak.")]
    [SerializeField] private float autoScaleSmoothing = 6f;

    [Header("Görünüm")]
    [Tooltip("Çizgi kalınlığı [px].")]
    [SerializeField] private float lineWidth = 3f;

    [Tooltip("Yatay ızgara çizgisi sayısı.")]
    [SerializeField] private int gridRows = 5;

    [Tooltip("Dikey ızgara çizgisi sayısı.")]
    [SerializeField] private int gridCols = 4;

    [SerializeField] private Color gridColor = new Color(1f, 1f, 1f, 0.13f);
    [SerializeField] private Color axisColor = new Color(1f, 1f, 1f, 0.30f);

    [Header("İmleç Okuması")]
    [Tooltip("Işın/fare grafiğin üzerine geldiğinde değerleri göster. " +
             "Kapatılırsa grafik ışına da yakalanmaz.")]
    [SerializeField] private bool hoverReadout = true;

    [Tooltip("İmleç konumundaki dikey kılavuz çizginin rengi.")]
    [SerializeField] private Color crosshairColor = new Color(1f, 1f, 1f, 0.45f);

    [Tooltip("Kılavuz çizginin kalınlığı [px].")]
    [SerializeField] private float crosshairWidth = 2f;

    [Tooltip("Seri üzerindeki işaretçinin kenar uzunluğu [px].")]
    [SerializeField] private float markerSize = 10f;

    [Tooltip("İşaretçinin çevresine çizilen koyu çerçevenin rengi.")]
    [SerializeField] private Color markerOutlineColor = new Color(0.07f, 0.09f, 0.15f, 0.95f);

    [Tooltip("Tooltip'te değerin yanına yazılacak birim, örn. \"rad\".")]
    [SerializeField] private string valueUnit = "";

    [Tooltip("Tooltip'teki değerlerin ondalık basamak sayısı.")]
    [Range(0, 6)]
    [SerializeField] private int valueDecimals = 4;

    // Pencere dışına düşen örnekler bu aralıkla temizlenir; her karede
    // listeyi taramanın anlamı yok.
    private const float PruneInterval = 0.5f;
    private float nextPruneTime;

    // Otomatik ölçekte kullanılan güncel sınırlar.
    private float currentYMin;
    private float currentYMax;

    // İmleç durumu. hoverAge01: 0 = sağ kenar (şimdi), 1 = sol kenar (en eski).
    private bool hoverActive;
    private float hoverAge01;
    private Vector2 hoverLocalPoint;
    private readonly List<HoverReading> hoverReadings = new List<HoverReading>();

    /// <summary>
    /// İmleç durumu ya da imleç altındaki değerler her değiştiğinde tetiklenir.
    /// Grafiğin üzerinde beklenirken veri kaydığı için her karede de tetiklenir.
    /// </summary>
    public event Action<HarmonyLineChart> HoverChanged;

    protected override void Awake()
    {
        base.Awake();
        currentYMin = yMin;
        currentYMax = yMax;

        // Işının grafiğe çarpabilmesi için graphic'in raycast hedefi olması şart.
        raycastTarget = hoverReadout;
    }

    protected override void OnDisable()
    {
        base.OnDisable();
        ClearHover();
    }

    /// <summary>Grafikteki seri sayısı.</summary>
    public int SeriesCount => series.Count;

    /// <summary>Gösterilen zaman penceresi [s].</summary>
    public float WindowSeconds => windowSeconds;

    /// <summary>Eksenin o anki alt sınırı. Eksen etiketlerini yazmak için kullanılır.</summary>
    public float CurrentYMin => currentYMin;

    /// <summary>Eksenin o anki üst sınırı.</summary>
    public float CurrentYMax => currentYMax;

    /// <summary>Otomatik ölçek açık mı?</summary>
    public bool AutoScaleEnabled => autoScaleY;

    /// <summary>İmleç şu anda grafiğin üzerinde mi?</summary>
    public bool IsHovered => hoverActive;

    /// <summary>
    /// İmlecin bulunduğu noktanın kaç saniye öncesine denk geldiği.
    /// Sağ kenarda 0, sol kenarda <see cref="WindowSeconds"/>.
    /// </summary>
    public float HoverAgeSeconds => hoverAge01 * windowSeconds;

    /// <summary>İmlecin dünya uzayındaki konumu; tooltip'i yerleştirmek için.</summary>
    public Vector3 HoverWorldPoint => rectTransform.TransformPoint(hoverLocalPoint);

    /// <summary>İmleç altındaki seri okumaları. İmleç dışarıdayken boştur.</summary>
    public IReadOnlyList<HoverReading> HoverReadings => hoverReadings;

    /// <summary>Değerlerin birimi, örn. "rad". Boş olabilir.</summary>
    public string ValueUnit
    {
        get => valueUnit;
        set => valueUnit = value ?? string.Empty;
    }

    /// <summary>Tooltip'te kullanılacak ondalık basamak sayısı.</summary>
    public int ValueDecimals => valueDecimals;

    /// <summary>
    /// Y ekseninin veriye göre kendiliğinden ölçeklenmesini açar.
    /// Sınır dışına çıkan değerlerin kırpılmasını önler.
    /// </summary>
    public void EnableAutoScale()
    {
        autoScaleY = true;
        SetVerticesDirty();
    }

    /// <summary>
    /// Seriyi etiketiyle birlikte tanımlar. Kurulum sırasında bir kez çağrılır.
    /// </summary>
    /// <param name="labels">Seri adları.</param>
    /// <param name="colors">Seri renkleri; labels ile aynı uzunlukta olmalı.</param>
    public void ConfigureSeries(string[] labels, Color[] colors)
    {
        series.Clear();
        hoverReadings.Clear();

        int count = Mathf.Min(labels.Length, colors.Length);
        for (int i = 0; i < count; i++)
            series.Add(new Series { label = labels[i], color = colors[i] });

        SetVerticesDirty();
    }

    /// <summary>
    /// Y ekseni sınırlarını sabitler ve otomatik ölçeği kapatır.
    /// </summary>
    /// <param name="min">Alt sınır.</param>
    /// <param name="max">Üst sınır.</param>
    public void SetYRange(float min, float max)
    {
        yMin = min; yMax = max;
        currentYMin = min; currentYMax = max;
        autoScaleY = false;
        SetVerticesDirty();
    }

    /// <summary>
    /// Bir seriye yeni örnek ekler.
    /// </summary>
    /// <param name="seriesIndex">Seri sırası.</param>
    /// <param name="value">Ölçüm değeri.</param>
    public void AddSample(int seriesIndex, float value)
    {
        if (seriesIndex < 0 || seriesIndex >= series.Count) return;
        series[seriesIndex].Points.Add(new Vector2(Time.time, value));
        SetVerticesDirty();
    }

    /// <summary>Tüm serilerin örneklerini siler.</summary>
    public void ClearSamples()
    {
        for (int i = 0; i < series.Count; i++) series[i].Points.Clear();
        SetVerticesDirty();
    }

    /// <summary>
    /// Serinin açıklama etiketi.
    /// </summary>
    /// <param name="index">Seri sırası.</param>
    public string GetLabel(int index)
        => (index >= 0 && index < series.Count) ? series[index].label : string.Empty;

    /// <summary>
    /// Serinin rengi.
    /// </summary>
    /// <param name="index">Seri sırası.</param>
    public Color GetColor(int index)
        => (index >= 0 && index < series.Count) ? series[index].color : Color.white;

    private void Update()
    {
        if (autoScaleY) UpdateAutoScale();

        // İmleç sabit dursa bile veri sola kaydığı için altındaki değer değişir;
        // okuma her karede tazelenmeli.
        if (hoverActive) RefreshHoverReadings();

        if (Time.time < nextPruneTime) return;
        nextPruneTime = Time.time + PruneInterval;

        float cutoff = Time.time - windowSeconds;
        bool removed = false;

        for (int i = 0; i < series.Count; i++)
        {
            var pts = series[i].Points;
            // Örnekler zaman sırasında geldiği için baştan silmek yeterli.
            int drop = 0;
            while (drop < pts.Count && pts[drop].x < cutoff) drop++;
            if (drop > 0) { pts.RemoveRange(0, drop); removed = true; }
        }

        if (removed) SetVerticesDirty();
    }

    // ---------------------------------------------------------------------
    // İmleç (kumanda ışını / fare)
    // ---------------------------------------------------------------------

    /// <summary>Işın grafiğe girdiğinde okumayı başlatır.</summary>
    /// <param name="eventData">Olay verisi.</param>
    public void OnPointerEnter(PointerEventData eventData) => UpdateHover(eventData);

    /// <summary>Işın grafiğin üzerinde gezinirken okuma noktasını taşır.</summary>
    /// <param name="eventData">Olay verisi.</param>
    public void OnPointerMove(PointerEventData eventData) => UpdateHover(eventData);

    /// <summary>Işın grafikten çıktığında okumayı kapatır.</summary>
    /// <param name="eventData">Olay verisi.</param>
    public void OnPointerExit(PointerEventData eventData) => ClearHover();

    /// <summary>
    /// Olay verisindeki imleç konumunu grafiğin yerel uzayına çevirip
    /// okumayı günceller. Konum grafiğin dışına düşerse okuma kapatılır.
    /// </summary>
    /// <param name="eventData">İşlenecek olay verisi.</param>
    private void UpdateHover(PointerEventData eventData)
    {
        if (!hoverReadout || !TryGetLocalPoint(eventData, out Vector2 local))
        {
            ClearHover();
            return;
        }

        Rect r = rectTransform.rect;
        if (local.x < r.xMin || local.x > r.xMax || local.y < r.yMin || local.y > r.yMax)
        {
            ClearHover();
            return;
        }

        hoverLocalPoint = local;
        hoverAge01 = Mathf.Clamp01((r.xMax - local.x) / Mathf.Max(r.width, 1e-4f));
        hoverActive = true;

        RefreshHoverReadings();
    }

    /// <summary>
    /// İmleç konumunu grafiğin yerel (rect) uzayında verir.
    /// </summary>
    /// <param name="eventData">Olay verisi.</param>
    /// <param name="local">Bulunan yerel konum.</param>
    /// <returns>Konum çözülebildiyse true.</returns>
    private bool TryGetLocalPoint(PointerEventData eventData, out Vector2 local)
    {
        local = Vector2.zero;
        if (eventData == null) return false;

        RaycastResult hit = eventData.pointerCurrentRaycast;

        // XR ışını: raycaster kesişimi zaten dünya uzayında verdiği için
        // ekran koordinatına gidip geri dönmeye gerek yok, bu yol daha kesin.
        if (hit.isValid && hit.module is TrackedDeviceGraphicRaycaster)
        {
            Vector3 p = rectTransform.InverseTransformPoint(hit.worldPosition);
            local = new Vector2(p.x, p.y);
            return true;
        }

        // Fare / dokunma: Editor'de gözlüksüz denemek için.
        Camera cam = eventData.enterEventCamera != null
            ? eventData.enterEventCamera
            : eventData.pressEventCamera;

        return RectTransformUtility.ScreenPointToLocalPointInRectangle(
            rectTransform, eventData.position, cam, out local);
    }

    /// <summary>Okumayı kapatır ve dinleyicilere haber verir.</summary>
    private void ClearHover()
    {
        if (!hoverActive && hoverReadings.Count == 0) return;

        hoverActive = false;
        hoverReadings.Clear();

        SetVerticesDirty();
        HoverChanged?.Invoke(this);
    }

    /// <summary>
    /// İmlecin denk geldiği zamanda her serinin en yakın örneğini toplar.
    /// Web arayüzündeki Chart.js "nearest / axis: x" davranışıyla aynıdır.
    /// </summary>
    private void RefreshHoverReadings()
    {
        hoverReadings.Clear();

        float t = Time.time - HoverAgeSeconds;

        for (int s = 0; s < series.Count; s++)
        {
            if (!TryFindNearest(series[s].Points, t, out Vector2 sample)) continue;
            hoverReadings.Add(new HoverReading(s, series[s].label, series[s].color, sample.y, sample.x));
        }

        SetVerticesDirty();
        HoverChanged?.Invoke(this);
    }

    /// <summary>
    /// Zaman sıralı örnek listesinde verilen ana en yakın örneği bulur.
    /// </summary>
    /// <param name="pts">(zaman, değer) örnekleri; zamana göre artan sırada.</param>
    /// <param name="t">Aranan zaman.</param>
    /// <param name="nearest">Bulunan örnek.</param>
    /// <returns>Liste boş değilse true.</returns>
    private static bool TryFindNearest(List<Vector2> pts, float t, out Vector2 nearest)
    {
        nearest = default;
        if (pts.Count == 0) return false;

        // Liste sıralı: t'yi geçen ilk noktada durup bir öncekiyle karşılaştırmak yeter.
        for (int i = 0; i < pts.Count; i++)
        {
            if (pts[i].x < t) continue;

            nearest = (i > 0 && (t - pts[i - 1].x) < (pts[i].x - t)) ? pts[i - 1] : pts[i];
            return true;
        }

        nearest = pts[pts.Count - 1];
        return true;
    }

    protected override void OnPopulateMesh(VertexHelper vh)
    {
        vh.Clear();

        Rect r = GetPixelAdjustedRect();
        float x0 = r.xMin, x1 = r.xMax;
        float y0 = r.yMin, y1 = r.yMax;

        DrawGrid(vh, x0, y0, x1, y1);

        float span = Mathf.Max(currentYMax - currentYMin, 1e-4f);
        float now = Time.time;

        for (int s = 0; s < series.Count; s++)
        {
            var pts = series[s].Points;
            if (pts.Count < 2) continue;

            Color32 col = series[s].color;
            Vector2 prev = Vector2.zero;
            bool hasPrev = false;

            for (int i = 0; i < pts.Count; i++)
            {
                float age = now - pts[i].x;
                if (age > windowSeconds) continue;

                // Sağ kenar "şimdi", sola doğru geçmiş.
                float tx = Mathf.Lerp(x1, x0, age / windowSeconds);
                float ny = Mathf.Clamp01((pts[i].y - currentYMin) / span);
                float ty = Mathf.Lerp(y0, y1, ny);

                Vector2 p = new Vector2(tx, ty);
                if (hasPrev) AddSegment(vh, prev, p, lineWidth, col);
                prev = p; hasPrev = true;
            }
        }

        // Kılavuz çizgi ve işaretçiler serilerin üstünde kalmalı; UI mesh'i
        // ekleme sırasına göre çizildiği için en sona bırakılıyor.
        if (hoverActive) DrawHoverOverlay(vh, x0, y0, x1, y1, span);
    }

    /// <summary>
    /// İmleç konumundaki dikey kılavuz çizgiyi ve her serinin okuma
    /// noktasındaki işaretçisini çizer.
    /// </summary>
    /// <param name="vh">Mesh yardımcısı.</param>
    /// <param name="x0">Çizim alanının sol kenarı.</param>
    /// <param name="y0">Alt kenar.</param>
    /// <param name="x1">Sağ kenar.</param>
    /// <param name="y1">Üst kenar.</param>
    /// <param name="span">Y ekseninin güncel aralığı.</param>
    private void DrawHoverOverlay(VertexHelper vh, float x0, float y0, float x1, float y1, float span)
    {
        float hx = Mathf.Lerp(x1, x0, hoverAge01);
        AddSegment(vh, new Vector2(hx, y0), new Vector2(hx, y1), crosshairWidth, crosshairColor);

        float half = markerSize * 0.5f;
        float outline = markerSize + 4f;
        float outlineHalf = outline * 0.5f;

        for (int i = 0; i < hoverReadings.Count; i++)
        {
            HoverReading reading = hoverReadings[i];

            float ny = Mathf.Clamp01((reading.Value - currentYMin) / span);
            float py = Mathf.Lerp(y0, y1, ny);

            // AddSegment kalınlığı olan bir quad ürettiği için, uzunluk ile
            // kalınlığı eşitleyince kare bir işaretçi çıkıyor.
            AddSegment(vh, new Vector2(hx - outlineHalf, py), new Vector2(hx + outlineHalf, py),
                       outline, markerOutlineColor);
            AddSegment(vh, new Vector2(hx - half, py), new Vector2(hx + half, py),
                       markerSize, reading.Color);
        }
    }

    /// <summary>
    /// Görünen penceredeki verilere göre eksen sınırlarını hedefe doğru
    /// yumuşatarak günceller. Sert sıçramalar eksenin titremesine yol açtığı
    /// için sınırlar hedefe üstel olarak yaklaştırılır.
    /// </summary>
    private void UpdateAutoScale()
    {
        float lo = float.MaxValue, hi = float.MinValue;
        float cutoff = Time.time - windowSeconds;
        bool any = false;

        for (int s = 0; s < series.Count; s++)
        {
            var pts = series[s].Points;
            for (int i = 0; i < pts.Count; i++)
            {
                if (pts[i].x < cutoff) continue;
                if (pts[i].y < lo) lo = pts[i].y;
                if (pts[i].y > hi) hi = pts[i].y;
                any = true;
            }
        }

        float targetMin, targetMax;

        if (!any)
        {
            // Henüz veri yok: tanımlı varsayılan aralığı koru.
            targetMin = yMin;
            targetMax = yMax;
        }
        else
        {
            float pad = Mathf.Max((hi - lo) * autoScalePadding, minAutoRange * 0.25f);
            targetMin = lo - pad;
            targetMax = hi + pad;

            // Veri neredeyse sabitse aralığı yapay olarak genişlet; aksi halde
            // gürültü tüm ekranı kaplar ve eksen sürekli oynar.
            float range = targetMax - targetMin;
            if (range < minAutoRange)
            {
                float mid = (targetMax + targetMin) * 0.5f;
                targetMin = mid - minAutoRange * 0.5f;
                targetMax = mid + minAutoRange * 0.5f;
            }
        }

        float k = 1f - Mathf.Exp(-Time.deltaTime * Mathf.Max(autoScaleSmoothing, 0.01f));
        float newMin = Mathf.Lerp(currentYMin, targetMin, k);
        float newMax = Mathf.Lerp(currentYMax, targetMax, k);

        // Anlamsız küçük değişikliklerde mesh'i yeniden kurma.
        if (Mathf.Abs(newMin - currentYMin) > 1e-4f || Mathf.Abs(newMax - currentYMax) > 1e-4f)
        {
            currentYMin = newMin;
            currentYMax = newMax;
            SetVerticesDirty();
        }
    }

    private void DrawGrid(VertexHelper vh, float x0, float y0, float x1, float y1)
    {
        Color32 g = gridColor;
        Color32 a = axisColor;

        for (int i = 0; i <= gridRows; i++)
        {
            float t = i / (float)gridRows;
            float y = Mathf.Lerp(y0, y1, t);
            AddSegment(vh, new Vector2(x0, y), new Vector2(x1, y), 1f, (i == 0) ? a : g);
        }

        for (int i = 0; i <= gridCols; i++)
        {
            float t = i / (float)gridCols;
            float x = Mathf.Lerp(x0, x1, t);
            AddSegment(vh, new Vector2(x, y0), new Vector2(x, y1), 1f, (i == 0) ? a : g);
        }
    }

    /// <summary>
    /// İki nokta arasına kalınlığı olan bir çizgi parçası (quad) ekler.
    /// </summary>
    private static void AddSegment(VertexHelper vh, Vector2 from, Vector2 to, float width, Color32 col)
    {
        Vector2 dir = to - from;
        float len = dir.magnitude;
        if (len < 1e-5f) return;

        dir /= len;
        Vector2 normal = new Vector2(-dir.y, dir.x) * (width * 0.5f);

        int idx = vh.currentVertCount;

        UIVertex v = UIVertex.simpleVert;
        v.color = col;

        v.position = from - normal; vh.AddVert(v);
        v.position = from + normal; vh.AddVert(v);
        v.position = to + normal;   vh.AddVert(v);
        v.position = to - normal;   vh.AddVert(v);

        vh.AddTriangle(idx, idx + 1, idx + 2);
        vh.AddTriangle(idx + 2, idx + 3, idx);
    }
}
