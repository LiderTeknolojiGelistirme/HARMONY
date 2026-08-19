using System;
using System.Collections.Concurrent;
using Newtonsoft.Json;
using RosSharp.RosBridgeClient;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using Zenject;

// System.String ile ROS# std_msgs/String aynı adı taşıdığı için takma ad kullanılır.
using RosString = RosSharp.RosBridgeClient.MessageTypes.Std.String;

/// <summary>
/// /harmony/scenario_event topic'inden gelen senaryo olayına karşılık, web
/// arayüzündekiyle aynı sistem uyarısını AR sahnesinde gösterir (KPI3 ölçümü).
///
/// Akış ROS tarafındaki app.py ile birebir aynıdır:
///   * sensing robotu son viewpoint'e varıp HOME'a dönmeden hemen önce
///     "LAST_VIEWPOINT_REACHED" yayınlar (sensing_robot.py).
///   * Uyarı tur başına yalnızca bir kez üretilir.
///   * Yeni tarama başlayınca (kusur listesi temizlenince) tur sıfırlanır.
///
/// Uyarı hiçbir süreci durdurmaz; operatör OK ile kapatır.
///
/// NOT: web arayüzü uyarıyı ROS'a değil kendi socket'ine yayınlar, dolayısıyla
/// dinlenebilecek hazır bir "fault" topic'i yoktur. Uyarı metni burada ROS
/// tarafındaki SCENARIO_FAULT sabitinin kopyasıdır; app.py'de değişirse burası
/// da güncellenmelidir.
/// </summary>
public class HarmonyScenarioFault : MonoBehaviour
{
    /// <summary>app.py içindeki SCENARIO_FAULT ile aynı olmalıdır.</summary>
    private const string FaultCode = "E-1042";
    private const string FaultMessage = "Lineer eksen haberleşmesi sağlanamıyor!";
    private const string FaultSeverity = "WARNING";

    /// <summary>Uyarıyı tetikleyen olay adı (sensing_robot.py).</summary>
    private const string TriggerEvent = "LAST_VIEWPOINT_REACHED";

    // Arayüz renkleri — HarmonyWebUI panelindeki kart diliyle aynı.
    private static readonly Color ColBackdrop = new Color32(0x05, 0x08, 0x12, 190);
    private static readonly Color ColCard     = new Color32(0x1a, 0x20, 0x35, 255);
    private static readonly Color ColAccent   = new Color32(0xf5, 0x9e, 0x0b, 255);
    private static readonly Color ColCode     = new Color32(0xef, 0x44, 0x44, 255);
    private static readonly Color ColText     = new Color32(0xf1, 0xf5, 0xf9, 255);
    private static readonly Color ColMuted    = new Color32(0x94, 0xa3, 0xb8, 255);

    [Inject] private RosConnector rosConnector;

    [Header("Kaynaklar")]
    [Tooltip("Tur sıfırlaması için kusur akışı. Boş bırakılırsa sahnede aranır.")]
    [SerializeField] private HarmonyDefectSubscriber defectSubscriber;

    [Tooltip("Modalın ekleneceği arayüz kökü. Boşsa HarmonyWebUI aranır.")]
    [SerializeField] private RectTransform uiRoot;

    [Header("Topic")]
    [Tooltip("Senaryo olaylarının yayınlandığı topic.")]
    [SerializeField] private string scenarioEventTopic = "/harmony/scenario_event";

    [Header("Modal (sahnede hazırsa buraya atayın)")]
    [Tooltip("Uyarı modalının kökü. Atanmışsa çalışma zamanında yeniden kurulmaz; " +
             "böylece OK düğmesinin OnClick bağlantısı Inspector'da görünür ve düzenlenebilir. " +
             "Boş bırakılırsa modal çalışma zamanında kurulur.")]
    [SerializeField] private GameObject modalRoot;

    [Tooltip("Hata kodunun yazıldığı alan (E-1042).")]
    [SerializeField] private TMP_Text codeText;

    [Tooltip("Hata açıklamasının yazıldığı alan.")]
    [SerializeField] private TMP_Text detailText;

    [Tooltip("Uyarı saatinin yazıldığı alan.")]
    [SerializeField] private TMP_Text timeText;

    [Header("Görünüm")]
    [Tooltip("Modal kartın genişlik ve yüksekliği (canvas birimi). " +
             "Yalnızca modal çalışma zamanında kurulurken kullanılır.")]
    [SerializeField] private Vector2 modalSize = new Vector2(880f, 520f);

    [Tooltip("Arkadaki paneli karartan örtü gösterilsin mi.")]
    [SerializeField] private bool showBackdrop = true;

    [Header("KPI3 Tepki Süresi Kaydı")]
    [Tooltip("Operatörün uyarıyı ne kadar sürede kapattığını cihaz diskine CSV olarak yaz.")]
    [SerializeField] private bool logReactionTime = true;

    [Tooltip("Kayıt dosyasının temel adı. Application.persistentDataPath altına açılır ve " +
             "sonuna açılış zamanı eklenir: harmony_kpi3_20260729_051533.csv. " +
             "Her uygulama açılışı kendi dosyasına yazar.")]
    [SerializeField] private string reactionLogFileName = "harmony_kpi3.csv";

    [Header("Hata Ayıklama")]
    [Tooltip("Gelen senaryo olaylarını konsola bas.")]
    [SerializeField] private bool verboseLogging;

    /// <summary>ROS geri çağrısı arka thread'den gelir.</summary>
    private readonly ConcurrentQueue<string> eventQueue = new ConcurrentQueue<string>();

    /// <summary>Bu turda uyarı üretildi mi? app.py'deki _fired_this_round karşılığı.</summary>
    private bool firedThisRound;

    /// <summary>Kaçıncı uyarı olduğunu sayar; app.py'deki _counter karşılığı.</summary>
    private int faultCounter;

    /// <summary>KPI3 tepki süresi yazıcısı; kayıt kapalıysa null.</summary>
    private HarmonyFaultLogger reactionLogger;

    /// <summary>Ekranda duran ve henüz kapatılmamış uyarının çıkış anı (T0).</summary>
    private DateTime? pendingFaultTime;

    /// <summary>Bekleyen uyarının sıra numarası.</summary>
    private int pendingFaultSeq;

    /// <summary>
    /// T0'dan itibaren geçen süreyi monotonik ölçer. Cihaz saati oynarsa
    /// duvar saati sıçrar, bu sayaç sıçramaz.
    /// </summary>
    private System.Diagnostics.Stopwatch reactionWatch;

    /// <summary>Kapatılmamış uyarı, uygulama kapanırken bir kez yazılsın diye.</summary>
    private bool pendingFlushed;

    private void Start()
    {
        if (defectSubscriber == null) defectSubscriber = FindObjectOfType<HarmonyDefectSubscriber>(true);

        if (logReactionTime)
        {
            // Uygulamanın açılış anı: şu andan, sahne yüklenmesinden bu yana
            // geçen süre çıkarılarak bulunur.
            DateTime appStart = DateTime.Now - TimeSpan.FromSeconds(Time.realtimeSinceStartup);
            reactionLogger = new HarmonyFaultLogger(reactionLogFileName, appStart);
            Debug.Log($"HarmonyScenarioFault: KPI3 kaydı → {reactionLogger.FilePath}");
        }

        // Modal sahnede hazırsa dokunma; OnClick bağlantısı Inspector'da durur.
        if (modalRoot == null)
        {
            if (uiRoot == null) uiRoot = FindUiRoot();

            if (uiRoot == null)
            {
                Debug.LogError("HarmonyScenarioFault: Arayüz kökü bulunamadı ve modal atanmamış; " +
                               "uyarı gösterilemeyecek. Inspector'dan modalRoot veya uiRoot atayın.");
                return;
            }

            BuildModal();
        }

        Hide();

        if (defectSubscriber != null)
            defectSubscriber.OnDefectsCleared += ResetRound;

        if (rosConnector?.RosSocket != null)
        {
            rosConnector.RosSocket.Subscribe<RosString>(scenarioEventTopic, ReceiveScenarioEvent);
            Debug.Log($"HarmonyScenarioFault: abone olundu → {scenarioEventTopic}");
        }
        else
        {
            Debug.LogWarning("HarmonyScenarioFault: RosSocket yok; senaryo uyarısı alınamayacak.");
        }
    }

    private void OnDestroy()
    {
        if (defectSubscriber != null)
            defectSubscriber.OnDefectsCleared -= ResetRound;
    }

    /// <summary>
    /// Modalın ekleneceği kökü arar. HARMONY paneli sahnede HarmonyWebUI adıyla
    /// durur; bulunamazsa sahnedeki ilk world-space canvas kullanılır.
    /// </summary>
    private RectTransform FindUiRoot()
    {
        var named = GameObject.Find("HarmonyWebUI");
        if (named != null && named.transform is RectTransform namedRect) return namedRect;

        foreach (var canvas in FindObjectsOfType<Canvas>(true))
        {
            if (canvas.renderMode != RenderMode.WorldSpace) continue;
            if (canvas.transform is RectTransform canvasRect) return canvasRect;
        }
        return null;
    }

    // =====================================================================
    // ROS
    // =====================================================================

    /// <summary>ROS geri çağrısı — arka thread. Sadece kuyruğa yazar.</summary>
    /// <param name="message">std_msgs/String gövdesi.</param>
    private void ReceiveScenarioEvent(RosString message)
    {
        if (message?.data == null) return;
        eventQueue.Enqueue(message.data);
    }

    private void Update()
    {
        while (eventQueue.TryDequeue(out string json))
            ProcessScenarioEvent(json);
    }

    /// <summary>
    /// Gelen olay gövdesini çözer ve tetikleyici olaysa uyarıyı üretir.
    /// </summary>
    /// <param name="json">Olay JSON'u.</param>
    private void ProcessScenarioEvent(string json)
    {
        string eventName;
        try
        {
            var dto = JsonConvert.DeserializeObject<HarmonyScenarioEventDto>(json);
            eventName = dto?.Event;
        }
        catch (JsonException e)
        {
            Debug.LogWarning($"HarmonyScenarioFault: olay çözülemedi ({e.Message}): {json}");
            return;
        }

        if (string.IsNullOrEmpty(eventName)) return;

        if (verboseLogging)
            Debug.Log($"HarmonyScenarioFault: olay alındı → {eventName}");

        if (!string.Equals(eventName.Trim(), TriggerEvent, StringComparison.OrdinalIgnoreCase))
            return;

        // Tur başına yalnızca bir kez; app.py ile aynı davranış.
        if (firedThisRound) return;
        firedThisRound = true;

        ShowFault();
    }

    /// <summary>Yeni tarama başladı; bu turda uyarı henüz üretilmedi.</summary>
    private void ResetRound()
    {
        firedThisRound = false;
    }

    // =====================================================================
    // Arayüz
    // =====================================================================

    /// <summary>Uyarıyı gösterir ve zaman damgasını yazar.</summary>
    public void ShowFault()
    {
        if (modalRoot == null) return;

        faultCounter++;

        DateTime t0 = DateTime.Now;

        if (codeText != null) codeText.text = FaultCode;
        if (detailText != null) detailText.text = FaultMessage;
        if (timeText != null) timeText.text = t0.ToString("HH:mm:ss");

        modalRoot.SetActive(true);

        // Tepki süresi ölçümünü başlat.
        pendingFaultTime = t0;
        pendingFaultSeq = faultCounter;
        pendingFlushed = false;
        reactionWatch = System.Diagnostics.Stopwatch.StartNew();

        // T0: uyarının üretildiği an — KPI3 için log'a düşer.
        Debug.Log($"[T0] {FaultCode} — {FaultMessage} " +
                  $"({FaultSeverity}, seq={faultCounter}, senaryo hatası, süreç durmadı)");
    }

    /// <summary>Operatör OK'ye bastı; uyarıyı kapatır ve tepki süresini yazar.</summary>
    public void Acknowledge()
    {
        // Ekranda bekleyen uyarı yoksa (örn. düğmeye boşuna basıldı) ölçüm yazma.
        if (pendingFaultTime.HasValue && !pendingFlushed)
        {
            DateTime ackTime = DateTime.Now;
            double reaction = reactionWatch != null
                ? reactionWatch.Elapsed.TotalSeconds
                : (ackTime - pendingFaultTime.Value).TotalSeconds;

            // T2: ROS tarafındaki app.py de operatör onayını T2 diye logluyor;
            // iki kaydın yan yana okunabilmesi için aynı adı kullanıyoruz.
            Debug.Log($"[T2] {FaultCode} operatör tarafından kapatıldı — " +
                      $"tepki süresi {reaction:0.000} s");

            reactionLogger?.Write(pendingFaultSeq, FaultCode, pendingFaultTime.Value, ackTime, reaction);

            pendingFlushed = true;
            pendingFaultTime = null;
            reactionWatch = null;
        }

        Hide();
    }

    /// <summary>
    /// Kapatılmamış uyarıyı kayda geçirir. Operatör OK'ye basmadan uygulamadan
    /// çıkarsa bu da bir ölçümdür: satır yazılır, tepki süresi boş bırakılır.
    /// </summary>
    private void FlushPendingFault()
    {
        if (!pendingFaultTime.HasValue || pendingFlushed) return;

        Debug.LogWarning($"HarmonyScenarioFault: {FaultCode} kapatılmadan oturum sonlandı.");
        reactionLogger?.Write(pendingFaultSeq, FaultCode, pendingFaultTime.Value, null, null);

        pendingFlushed = true;
    }

    private void OnApplicationQuit()
    {
        FlushPendingFault();
    }

    private void OnApplicationPause(bool paused)
    {
        // Android'de OnApplicationQuit her zaman çağrılmaz; arka plana alınma
        // daha güvenilir bir kayıt noktasıdır.
        if (paused) FlushPendingFault();
    }

    private void Hide()
    {
        if (modalRoot != null) modalRoot.SetActive(false);
    }

    /// <summary>Modalı çalışma zamanında kurar.</summary>
    private void BuildModal()
    {
        modalRoot = new GameObject("ScenarioFaultModal", typeof(RectTransform));
        var rootRect = (RectTransform)modalRoot.transform;
        rootRect.SetParent(uiRoot, false);
        Stretch(rootRect);
        rootRect.SetAsLastSibling();

        if (showBackdrop)
        {
            var backdrop = CreateImage("Backdrop", rootRect, ColBackdrop);
            Stretch(backdrop.rectTransform);
        }

        // --- Kart ---
        var card = CreateImage("Card", rootRect, ColCard);
        card.rectTransform.sizeDelta = modalSize;
        card.rectTransform.anchoredPosition = Vector2.zero;

        // Kenarlık: kartın biraz büyüğü, arkasında vurgu renginde.
        var border = CreateImage("Border", rootRect, ColAccent);
        border.rectTransform.sizeDelta = modalSize + new Vector2(8f, 8f);
        border.rectTransform.anchoredPosition = Vector2.zero;
        border.transform.SetSiblingIndex(card.transform.GetSiblingIndex());

        float half = modalSize.y * 0.5f;

        CreateWarningIcon(card.rectTransform, new Vector2(0f, half - 96f));

        CreateText("Title", card.rectTransform, "SYSTEM WARNING", 44f, ColAccent,
            new Vector2(0f, half - 190f), new Vector2(modalSize.x, 56f));

        codeText = CreateText("Code", card.rectTransform, FaultCode, 56f, ColCode,
            new Vector2(0f, half - 262f), new Vector2(modalSize.x, 68f));

        detailText = CreateText("Detail", card.rectTransform, FaultMessage, 36f, ColText,
            new Vector2(0f, half - 340f), new Vector2(modalSize.x - 80f, 90f));
        detailText.enableWordWrapping = true;

        timeText = CreateText("Time", card.rectTransform, string.Empty, 26f, ColMuted,
            new Vector2(0f, half - 402f), new Vector2(modalSize.x, 36f));

        CreateOkButton(card.rectTransform, new Vector2(0f, -half + 62f));
    }

    /// <summary>
    /// Uyarı ikonunu kurar: 45° döndürülmüş elmas ve içinde ünlem.
    ///
    /// Sembol karakteri (U+26A0 vb.) kullanılmaz; projenin varsayılan fontu
    /// LiberationSans SDF bu gliflere sahip değil ve ekranda boş kare çıkıyor.
    /// Ünlem işareti her fontta bulunur.
    /// </summary>
    /// <param name="parent">Kart gövdesi.</param>
    /// <param name="position">İkonun kart içindeki konumu.</param>
    private static void CreateWarningIcon(RectTransform parent, Vector2 position)
    {
        var diamond = CreateImage("IconShape", parent, ColAccent);
        diamond.rectTransform.sizeDelta = new Vector2(84f, 84f);
        diamond.rectTransform.anchoredPosition = position;
        diamond.rectTransform.localRotation = Quaternion.Euler(0f, 0f, 45f);

        // Ünlem elmasla birlikte dönmesin diye karta doğrudan bağlanır.
        var mark = CreateText("IconMark", parent, "!", 58f,
            new Color32(0x0d, 0x12, 0x25, 255), position, new Vector2(84f, 84f));
        mark.fontStyle = FontStyles.Bold;
    }

    /// <summary>OK düğmesini kurar.</summary>
    /// <param name="parent">Kart gövdesi.</param>
    /// <param name="position">Düğmenin kart içindeki konumu.</param>
    private void CreateOkButton(RectTransform parent, Vector2 position)
    {
        var image = CreateImage("BtnOk", parent, ColAccent);
        image.rectTransform.sizeDelta = new Vector2(280f, 76f);
        image.rectTransform.anchoredPosition = position;

        var button = image.gameObject.AddComponent<Button>();
        button.targetGraphic = image;
        button.onClick.AddListener(Acknowledge);

        var label = CreateText("Text", image.rectTransform, "OK", 34f,
            new Color32(0x0d, 0x12, 0x25, 255), Vector2.zero, new Vector2(280f, 76f));
        label.fontStyle = FontStyles.Bold;
    }

    /// <summary>Ortaya sabitlenmiş bir Image üretir.</summary>
    private static Image CreateImage(string name, RectTransform parent, Color color)
    {
        var go = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
        var rect = (RectTransform)go.transform;
        rect.SetParent(parent, false);
        Center(rect);

        var image = go.GetComponent<Image>();
        image.color = color;
        return image;
    }

    /// <summary>Ortaya sabitlenmiş bir TextMeshPro etiketi üretir.</summary>
    private static TMP_Text CreateText(
        string name, RectTransform parent, string content,
        float fontSize, Color color, Vector2 position, Vector2 size)
    {
        var go = new GameObject(name, typeof(RectTransform));
        var rect = (RectTransform)go.transform;
        rect.SetParent(parent, false);
        Center(rect);
        rect.sizeDelta = size;
        rect.anchoredPosition = position;

        var text = go.AddComponent<TextMeshProUGUI>();
        text.text = content;
        text.fontSize = fontSize;
        text.color = color;
        text.alignment = TextAlignmentOptions.Center;
        text.enableWordWrapping = false;

        // Etiketler süstür; tıklamayı düğmenin kendisi almalı. Aksi halde isabet
        // önce yazıya düşer ve olayın ebeveyne yükselmesine bel bağlanmış olur.
        text.raycastTarget = false;
        return text;
    }

    /// <summary>RectTransform'u ebeveynin ortasına sabitler.</summary>
    private static void Center(RectTransform rect)
    {
        rect.anchorMin = new Vector2(0.5f, 0.5f);
        rect.anchorMax = new Vector2(0.5f, 0.5f);
        rect.pivot = new Vector2(0.5f, 0.5f);
    }

    /// <summary>RectTransform'u ebeveyni tamamen kaplayacak şekilde gerer.</summary>
    private static void Stretch(RectTransform rect)
    {
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.pivot = new Vector2(0.5f, 0.5f);
        rect.offsetMin = Vector2.zero;
        rect.offsetMax = Vector2.zero;
    }
}

/// <summary>
/// /harmony/scenario_event gövdesi (sensing_robot.py).
/// </summary>
[Serializable]
public class HarmonyScenarioEventDto
{
    [JsonProperty("event")] public string Event;
    [JsonProperty("note")] public string Note;
    [JsonProperty("timestamp")] public string Timestamp;
}
