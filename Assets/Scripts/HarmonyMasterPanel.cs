using System.Collections.Generic;
using System.Text;
using TMPro;
using UnityEngine;

/// <summary>
/// Usta (MASTER) panelini sürer.
///
/// Web arayüzündeki bilgi çubuğunun (Current Mode / Defects Detected), kusur
/// tablosunun ve sistem log'unun AR karşılığıdır. Komut butonu barındırmaz —
/// aksiyon çırak panelindedir.
///
/// Tablo sütunları TMP'nin &lt;pos=%&gt; etiketiyle hizalanır; başlık satırı
/// sahnedeki TableHeader nesnesinde aynı yüzdelerle tanımlıdır, ikisi
/// değiştirilirse birlikte değiştirilmelidir.
/// </summary>
public class HarmonyMasterPanel : MonoBehaviour
{
    // Tablo sütunlarının yatay konumları. Sahnedeki TableHeader ile birebir aynı.
    private const string ColId = "<pos=0%>";
    private const string ColType = "<pos=18%>";
    private const string ColX = "<pos=35%>";
    private const string ColY = "<pos=49%>";
    private const string ColZ = "<pos=63%>";
    private const string ColStatus = "<pos=77%>";

    [Header("Bağımlılıklar")]
    [Tooltip("Kusur tablosu kaynağı. Boş bırakılırsa sahnede aranır.")]
    [SerializeField] private HarmonyDefectSubscriber defectSubscriber;

    [Tooltip("Görev durumu kaynağı. Boş bırakılırsa sahnede aranır.")]
    [SerializeField] private HarmonyMissionController missionController;

    [Header("UI Alanları")]
    [Tooltip("CURRENT MODE kartının değer alanı.")]
    [SerializeField] private TMP_Text statusText;

    [Tooltip("DEFECTS DETECTED kartının değer alanı.")]
    [SerializeField] private TMP_Text defectCountText;

    [Tooltip("Kusur tablosunun gövdesi.")]
    [SerializeField] private TMP_Text defectListText;

    [Tooltip("Sistem log alanı.")]
    [SerializeField] private TMP_Text logText;

    [Header("Görünüm")]
    [Tooltip("Kusur satırlarında koordinatları göster.")]
    [SerializeField] private bool showCoordinates = true;

    [Tooltip("Koordinatların ondalık basamak sayısı.")]
    [SerializeField] private int coordinateDecimals = 3;

    [Tooltip("Log alanında tutulacak en fazla satır sayısı.")]
    [SerializeField] private int maxLogLines = 10;

    private readonly StringBuilder builder = new StringBuilder(512);
    private readonly List<string> logLines = new List<string>();

    private bool needsRedraw = true;

    private void Start()
    {
        if (defectSubscriber == null)
            defectSubscriber = FindObjectOfType<HarmonyDefectSubscriber>();

        if (missionController == null)
            missionController = FindObjectOfType<HarmonyMissionController>();

        if (defectSubscriber != null)
        {
            defectSubscriber.OnDefectAdded += HandleDefectAdded;
            defectSubscriber.OnDefectUpdated += HandleDefectUpdated;
            defectSubscriber.OnDefectsCleared += HandleDefectsCleared;
        }
        else
        {
            Debug.LogWarning("HarmonyMasterPanel: HarmonyDefectSubscriber bulunamadı, " +
                             "kusur tablosu boş kalacak.");
        }

        if (missionController != null)
        {
            missionController.OnMissionStateChanged += HandleMissionStateChanged;
            missionController.OnCommandSent += HandleCommandSent;
        }

        AppendLog("SYSTEM", "Dashboard started. Ready to operate.", "#2dd4bf");
        needsRedraw = true;
    }

    private void OnDestroy()
    {
        if (defectSubscriber != null)
        {
            defectSubscriber.OnDefectAdded -= HandleDefectAdded;
            defectSubscriber.OnDefectUpdated -= HandleDefectUpdated;
            defectSubscriber.OnDefectsCleared -= HandleDefectsCleared;
        }

        if (missionController != null)
        {
            missionController.OnMissionStateChanged -= HandleMissionStateChanged;
            missionController.OnCommandSent -= HandleCommandSent;
        }
    }

    // ---------------------------------------------------------------------
    // Olay işleyicileri
    // ---------------------------------------------------------------------

    private void HandleDefectAdded(HarmonyDefect defect)
    {
        AppendLog("SENSING", $"{defect.Id} detected ({defect.DefectType}).", "#06b6d4");
        needsRedraw = true;
    }

    private void HandleDefectUpdated(HarmonyDefect defect)
    {
        AppendLog("CLEANING", $"{defect.Id} → {StatusToPlainText(defect.Status)}.", "#a855f7");
        needsRedraw = true;
    }

    private void HandleDefectsCleared()
    {
        AppendLog("SYSTEM", "Defect table cleared, starting new cycle.", "#2dd4bf");
        needsRedraw = true;
    }

    private void HandleMissionStateChanged(MissionState state, string note)
    {
        string message = string.IsNullOrWhiteSpace(note)
            ? HarmonyMissionController.StateToDisplayText(state)
            : $"{HarmonyMissionController.StateToDisplayText(state)} — {note}";

        AppendLog("MISSION", message, "#a855f7");
        needsRedraw = true;
    }

    private void HandleCommandSent(string cmd)
    {
        AppendLog("OPERATOR", $"Command sent: {cmd}", "#f59e0b");
        needsRedraw = true;
    }

    private void LateUpdate()
    {
        // Sensing sırasında kusurlar 0.3 sn arayla art arda gelir; aynı karede
        // birden fazla olay olsa da tabloyu bir kez çiziyoruz.
        if (!needsRedraw) return;
        needsRedraw = false;

        RedrawStatus();
        RedrawDefectList();
        RedrawLog();
    }

    // ---------------------------------------------------------------------
    // Çizim
    // ---------------------------------------------------------------------

    /// <summary>
    /// CURRENT MODE ve DEFECTS DETECTED kartlarını yeniler.
    /// </summary>
    private void RedrawStatus()
    {
        MissionState state = missionController != null ? missionController.State : MissionState.Unknown;
        int total = defectSubscriber != null ? defectSubscriber.DefectCount : 0;
        int cleaned = defectSubscriber != null ? defectSubscriber.CleanedCount : 0;

        if (statusText != null)
            statusText.text = HarmonyMissionController.StateToDisplayText(state);

        if (defectCountText != null)
        {
            defectCountText.text = cleaned > 0
                ? $"{total}  <size=60%><color=#10b981>({cleaned} cleaned)</color></size>"
                : total.ToString();
        }
    }

    /// <summary>
    /// Kusur tablosunu yeniden oluşturur. Satır sırası ROS'tan geliş sırasıdır.
    /// </summary>
    private void RedrawDefectList()
    {
        if (defectListText == null) return;

        if (defectSubscriber == null || defectSubscriber.DefectCount == 0)
        {
            defectListText.text = "<i><color=#64748b>No defects reported yet. Press START SCAN.</color></i>";
            return;
        }

        builder.Length = 0;
        string format = "F" + Mathf.Clamp(coordinateDecimals, 0, 6);

        var defects = defectSubscriber.Defects;
        for (int i = 0; i < defects.Count; i++)
        {
            HarmonyDefect d = defects[i];

            builder.Append(ColId).Append("<b>").Append(d.Id).Append("</b>")
                   .Append(ColType).Append(d.DefectType);

            if (showCoordinates)
            {
                builder.Append(ColX).Append(d.RosPosition.x.ToString(format))
                       .Append(ColY).Append(d.RosPosition.y.ToString(format))
                       .Append(ColZ).Append(d.RosPosition.z.ToString(format));
            }

            builder.Append(ColStatus).Append(StatusToRichText(d.Status));

            if (i < defects.Count - 1)
                builder.Append('\n');
        }

        defectListText.text = builder.ToString();
    }

    private void RedrawLog()
    {
        if (logText == null) return;

        logText.text = BuildLogText();

        // Satırlar kelime kaydırmayla ikiye bölünebildiği için sabit satır
        // sayısı taşmayı engellemiyor; gerçek yüksekliğe bakıp sığana kadar
        // en eski satırları düşürüyoruz.
        float available = logText.rectTransform.rect.height;
        int guard = 0;

        while (logLines.Count > 1 && guard++ < 64 && logText.preferredHeight > available)
        {
            logLines.RemoveAt(0);
            logText.text = BuildLogText();
        }
    }

    /// <summary>
    /// Log satırlarını tek metne birleştirir.
    /// </summary>
    private string BuildLogText()
    {
        builder.Length = 0;
        for (int i = 0; i < logLines.Count; i++)
        {
            builder.Append(logLines[i]);
            if (i < logLines.Count - 1) builder.Append('\n');
        }
        return builder.ToString();
    }

    /// <summary>
    /// Log alanına zaman damgalı bir satır ekler. En eski satırlar
    /// <see cref="maxLogLines"/> sınırını aşınca düşer.
    /// </summary>
    /// <param name="source">Kaynak etiketi, örn. "MISSION".</param>
    /// <param name="message">Log metni.</param>
    /// <param name="colorHex">Kaynak etiketinin rengi, "#rrggbb".</param>
    private void AppendLog(string source, string message, string colorHex)
    {
        string stamp = System.DateTime.Now.ToString("HH:mm:ss");
        logLines.Add($"<color=#64748b>{stamp}</color>  <color={colorHex}><b>{source}</b></color>  {message}");

        int overflow = logLines.Count - Mathf.Max(maxLogLines, 1);
        if (overflow > 0)
            logLines.RemoveRange(0, overflow);
    }

    /// <summary>
    /// Kusur durumunu web arayüzündeki rozet renkleriyle eşleşen etikete çevirir.
    /// </summary>
    /// <param name="status">Kusur durumu.</param>
    private static string StatusToRichText(DefectStatus status)
    {
        switch (status)
        {
            case DefectStatus.Detected:
                return "<color=#ef4444><b>DETECTED</b></color>";
            case DefectStatus.Cleaning:
                return "<color=#f59e0b><b>CLEANING</b></color>";
            case DefectStatus.Cleaned:
                return "<color=#10b981><b>CLEANED</b></color>";
            default:
                return "<color=#64748b>—</color>";
        }
    }

    /// <summary>
    /// Durumun log satırında kullanılacak düz metin karşılığı.
    /// </summary>
    /// <param name="status">Kusur durumu.</param>
    private static string StatusToPlainText(DefectStatus status)
    {
        switch (status)
        {
            case DefectStatus.Detected: return "DETECTED";
            case DefectStatus.Cleaning: return "CLEANING";
            case DefectStatus.Cleaned: return "CLEANED";
            default: return "—";
        }
    }
}
