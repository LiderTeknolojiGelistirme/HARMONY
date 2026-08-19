using System.Text;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Grafiklerin üzerinde gezinen kumanda ışınının bulunduğu noktadaki sayısal
/// değerleri gösteren kutu — web arayüzündeki Chart.js tooltip'inin karşılığı.
///
/// Dinlediği <see cref="HarmonyLineChart"/> bileşenlerinden biri imleç altına
/// girdiğinde kutu görünür, imlecin yanına yerleşir ve her karede tazelenir.
/// Tek bir ışın olduğu için aynı anda yalnızca bir grafik etkin olabilir.
///
/// Kutu, konumlandırma alanı olarak kendi RectTransform'unu kullanır; bu
/// yüzden bileşen grafiklerle aynı panele (HarmonyChartsUI) gerilmiş bir
/// nesnede durmalı ve hiyerarşide grafiklerden sonra gelmelidir.
/// </summary>
public class HarmonyChartTooltip : MonoBehaviour
{
    [Header("Bağımlılıklar")]
    [Tooltip("Dinlenecek grafikler. Boş bırakılırsa aynı panelde aranır.")]
    [SerializeField] private HarmonyLineChart[] charts;

    [Tooltip("Taşınan ve boyutlanan kutu. Bu bileşenin altında olmalı.")]
    [SerializeField] private RectTransform box;

    [Tooltip("Kutunun içindeki metin alanı.")]
    [SerializeField] private TMP_Text label;

    [Header("Yerleşim")]
    [Tooltip("Metnin kutu kenarlarına uzaklığı [px].")]
    [SerializeField] private Vector2 padding = new Vector2(16f, 12f);

    [Tooltip("Kutunun imlece göre kayması [px]. Y negatifse imlecin altına açılır.")]
    [SerializeField] private Vector2 pointerOffset = new Vector2(24f, -24f);

    [Tooltip("Kutunun panel kenarına en fazla yaklaşabileceği mesafe [px].")]
    [SerializeField] private float edgeMargin = 8f;

    [Tooltip("Kutunun en dar hali [px].")]
    [SerializeField] private float minWidth = 180f;

    [Header("Görünüm")]
    [Tooltip("Başlık satırının rengi (zaman etiketi).")]
    [SerializeField] private Color titleColor = new Color32(0xf1, 0xf5, 0xf9, 255);

    // Metnin doğal genişliğini ölçerken kullanılan üst sınır. Satır kaydırma
    // kapalı olduğu için ölçüme girmez, yalnızca TMP'nin sıfır genişlikte
    // kaydırmaya kalkmasını önler.
    private const float MeasureWidth = 4096f;

    // Konumlandırma alanı — kutunun sınırlandırıldığı dikdörtgen.
    private RectTransform area;

    // Şu anda okunan grafik; imleç dışarı çıkınca null olur.
    private HarmonyLineChart active;

    private readonly StringBuilder builder = new StringBuilder(256);

    private void Awake()
    {
        area = (RectTransform)transform;

        if (box == null)
            Debug.LogWarning("HarmonyChartTooltip: kutu (box) atanmamış, tooltip çalışmayacak.");

        if (label == null && box != null)
            label = box.GetComponentInChildren<TMP_Text>(true);

        if (charts == null || charts.Length == 0)
            charts = transform.parent != null
                ? transform.parent.GetComponentsInChildren<HarmonyLineChart>(true)
                : new HarmonyLineChart[0];

        // Tooltip ışını kesmemeli; aksi halde kendi kendini yakalayıp
        // grafiğin okumasını sürekli kapatır.
        if (box != null)
        {
            foreach (var graphic in box.GetComponentsInChildren<Graphic>(true))
                graphic.raycastTarget = false;
        }

        Hide();
    }

    private void OnEnable()
    {
        if (charts == null) return;

        for (int i = 0; i < charts.Length; i++)
            if (charts[i] != null) charts[i].HoverChanged += HandleHoverChanged;
    }

    private void OnDisable()
    {
        if (charts == null) return;

        for (int i = 0; i < charts.Length; i++)
            if (charts[i] != null) charts[i].HoverChanged -= HandleHoverChanged;

        Hide();
    }

    /// <summary>
    /// Bir grafiğin imleç durumu değiştiğinde kutuyu günceller.
    /// </summary>
    /// <param name="chart">Olayı yayan grafik.</param>
    private void HandleHoverChanged(HarmonyLineChart chart)
    {
        if (chart == null) return;

        if (!chart.IsHovered)
        {
            // Başka bir grafiğe geçilmişse eski grafiğin çıkış olayı kutuyu kapatmasın.
            if (active == chart) Hide();
            return;
        }

        active = chart;
        Show(chart);
    }

    /// <summary>Kutuyu gizler.</summary>
    private void Hide()
    {
        active = null;
        if (box != null && box.gameObject.activeSelf) box.gameObject.SetActive(false);
    }

    /// <summary>
    /// Kutuyu verilen grafiğin okumalarıyla doldurur, boyutlandırır ve imlecin
    /// yanına yerleştirir.
    /// </summary>
    /// <param name="chart">Okumaları gösterilecek grafik.</param>
    private void Show(HarmonyLineChart chart)
    {
        if (box == null || label == null) return;

        var readings = chart.HoverReadings;
        if (readings.Count == 0)
        {
            // Grafiğe henüz veri gelmemiş; boş kutu göstermenin anlamı yok.
            Hide();
            return;
        }

        string content = BuildContent(chart);

        // Metnin doğal boyutunu metni atamadan önce ölçüyoruz; iki geçiş
        // yerine tek geçişte hem kutu hem metin alanı boyutlanıyor.
        Vector2 textSize = label.GetPreferredValues(content, MeasureWidth, 0f);
        label.text = content;

        float width = Mathf.Max(textSize.x, minWidth);
        box.sizeDelta = new Vector2(width + padding.x * 2f, textSize.y + padding.y * 2f);

        var labelRect = label.rectTransform;
        labelRect.anchoredPosition = new Vector2(padding.x, -padding.y);
        labelRect.sizeDelta = new Vector2(width, textSize.y);

        if (!box.gameObject.activeSelf) box.gameObject.SetActive(true);

        PlaceNearPointer(chart, box.sizeDelta);
    }

    /// <summary>
    /// Tooltip metnini kurar: başlıkta imlecin kaç saniye öncesine denk geldiği,
    /// gövdede her serinin rengi, etiketi ve o andaki değeri.
    /// </summary>
    /// <param name="chart">Okumaları alınacak grafik.</param>
    private string BuildContent(HarmonyLineChart chart)
    {
        builder.Length = 0;

        string titleHex = ColorUtility.ToHtmlStringRGB(titleColor);
        string format = "F" + Mathf.Clamp(chart.ValueDecimals, 0, 6);
        string unit = string.IsNullOrEmpty(chart.ValueUnit) ? string.Empty : " " + chart.ValueUnit;

        // Web'deki x ekseni gibi "şimdi"ye göre negatif saniye.
        builder.Append("<color=#").Append(titleHex).Append("><b>")
               .Append((-chart.HoverAgeSeconds).ToString("F1")).Append(" s</b></color>");

        var readings = chart.HoverReadings;
        for (int i = 0; i < readings.Count; i++)
        {
            var r = readings[i];

            builder.Append('\n')
                   .Append("<color=#").Append(ColorUtility.ToHtmlStringRGB(r.Color)).Append(">■</color> ")
                   .Append(r.Label).Append(": <b>").Append(r.Value.ToString(format)).Append("</b>")
                   .Append(unit);
        }

        return builder.ToString();
    }

    /// <summary>
    /// Kutuyu imlecin yanına koyar. Panel kenarına taşacaksa imlecin diğer
    /// tarafına geçer, yine sığmazsa alanın içine kırpılır.
    /// </summary>
    /// <param name="chart">İmlecin bulunduğu grafik.</param>
    /// <param name="size">Kutunun güncel boyutu.</param>
    private void PlaceNearPointer(HarmonyLineChart chart, Vector2 size)
    {
        Vector2 pointer = area.InverseTransformPoint(chart.HoverWorldPoint);
        Rect bounds = area.rect;

        // Kutunun pivotu sol-üst: x sol kenar, y üst kenardır.
        float x = pointer.x + pointerOffset.x;
        if (x + size.x > bounds.xMax - edgeMargin)
            x = pointer.x - pointerOffset.x - size.x;

        float y = pointer.y + pointerOffset.y;
        if (y - size.y < bounds.yMin + edgeMargin)
            y = pointer.y - pointerOffset.y + size.y;

        float xMax = Mathf.Max(bounds.xMin + edgeMargin, bounds.xMax - edgeMargin - size.x);
        float yMin = Mathf.Min(bounds.yMax - edgeMargin, bounds.yMin + edgeMargin + size.y);

        box.anchoredPosition = new Vector2(
            Mathf.Clamp(x, bounds.xMin + edgeMargin, xMax),
            Mathf.Clamp(y, yMin, bounds.yMax - edgeMargin));
    }
}
