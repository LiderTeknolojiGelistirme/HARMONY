using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Komut çubuğunu ve senaryo durum göstergelerini sürer.
///
/// Web arayüzündeki komut çubuğunun (START / CONFIRM / REINSPECT / STOP) ve
/// durum satırının AR karşılığıdır. Butonlar Unity'nin yerleşik
/// <see cref="Button"/> bileşenidir; Modern UI Pack bağımlılığı yoktur.
/// </summary>
public class HarmonyApprenticePanel : MonoBehaviour
{
    // Durum renkleri — web arayüzündeki rozet renkleriyle aynı.
    private static readonly Color ColIdle     = new Color32(0x94, 0xa3, 0xb8, 255);
    private static readonly Color ColSensing  = new Color32(0x06, 0xb6, 0xd4, 255);
    private static readonly Color ColWaiting  = new Color32(0xf5, 0x9e, 0x0b, 255);
    private static readonly Color ColCleaning = new Color32(0xa8, 0x55, 0xf7, 255);
    private static readonly Color ColOk       = new Color32(0x10, 0xb9, 0x81, 255);
    private static readonly Color ColOff      = new Color32(0xef, 0x44, 0x44, 255);

    [Header("Bağımlılıklar")]
    [Tooltip("Komutları yayınlayan bileşen. Boş bırakılırsa sahnede aranır.")]
    [SerializeField] private HarmonyMissionController missionController;

    [Header("Komut Butonları")]
    [SerializeField] private Button startButton;
    [SerializeField] private Button confirmButton;
    [SerializeField] private Button reinspectButton;
    [SerializeField] private Button stopButton;

    [Header("Senaryo Durum Çubuğu")]
    [SerializeField] private TMP_Text hilStatusText;
    [SerializeField] private Image hilDot;
    [SerializeField] private TMP_Text missionStatusText;
    [SerializeField] private Image missionDot;
    [SerializeField] private TMP_Text scenarioNameText;

    [Header("Onay Bandı")]
    [SerializeField] private GameObject confirmBannerBg;
    [SerializeField] private GameObject confirmBannerText;

    [Header("Yönerge")]
    [SerializeField] private TMP_Text instructionText;

    [Header("Senaryo")]
    [Tooltip("Panelde gösterilecek senaryo adı.")]
    [SerializeField] private string scenarioName = "Sensing & Cleaning";

    private void Start()
    {
        if (missionController == null)
            missionController = FindObjectOfType<HarmonyMissionController>();

        if (missionController == null)
        {
            Debug.LogError("HarmonyApprenticePanel: HarmonyMissionController bulunamadı! " +
                           "Butonlar komut gönderemeyecek.");
            return;
        }

        // NOT: Komut butonlarının onClick bağlantıları Inspector'da kalıcı
        // (persistent) olarak tanımlıdır; ExecCommandViaSocket.Send çağrılır.
        // Burada ayrıca dinleyici eklenmez, yoksa her tıklamada iki mesaj gider.

        missionController.OnMissionStateChanged += HandleMissionStateChanged;

        if (scenarioNameText != null)
            scenarioNameText.text = scenarioName;

        HandleMissionStateChanged(missionController.State, missionController.Note);
    }

    private void OnDestroy()
    {
        if (missionController != null)
            missionController.OnMissionStateChanged -= HandleMissionStateChanged;
    }

    private void Update()
    {
        RefreshHilStatus();
    }

    /// <summary>
    /// ROS köprüsü bağlantı göstergesini günceller.
    /// </summary>
    private void RefreshHilStatus()
    {
        // Demo kipinde rosbridge'in kapalı olması beklenen durumdur; kırmızı
        // OFFLINE göstermek yanıltıcı olur.
        if (missionController != null && missionController.DemoMode)
        {
            if (hilStatusText != null)
            {
                hilStatusText.text = "DEMO";
                hilStatusText.color = ColWaiting;
            }

            if (hilDot != null) hilDot.color = ColWaiting;
            return;
        }

        bool connected = missionController != null && missionController.IsRosConnected;

        if (hilStatusText != null)
        {
            hilStatusText.text = connected ? "CONNECTED" : "OFFLINE";
            hilStatusText.color = connected ? ColOk : ColOff;
        }

        if (hilDot != null)
            hilDot.color = connected ? ColOk : ColOff;
    }

    /// <summary>
    /// Görev durumuna göre durum çubuğunu, onay bandını ve yönergeyi günceller.
    /// </summary>
    /// <param name="state">Güncel görev durumu.</param>
    /// <param name="note">ROS'un gönderdiği açıklama.</param>
    private void HandleMissionStateChanged(MissionState state, string note)
    {
        string stateLabel = HarmonyMissionController.StateToDisplayText(state);
        Color stateColor = StateToColor(state);

        if (missionStatusText != null)
        {
            missionStatusText.text = stateLabel;
            missionStatusText.color = stateColor;
        }

        if (missionDot != null)
            missionDot.color = stateColor;

        bool awaitingConfirm = state == MissionState.Waiting;
        if (confirmBannerBg != null) confirmBannerBg.SetActive(awaitingConfirm);
        if (confirmBannerText != null) confirmBannerText.SetActive(awaitingConfirm);

        if (instructionText == null) return;

        string instruction = string.IsNullOrWhiteSpace(note)
            ? DefaultInstruction(state)
            : note;

        instructionText.text = $"<b>{stateLabel}</b>\n{instruction}";
    }

    /// <summary>Görev durumunun gösterge rengi.</summary>
    /// <param name="state">Görev durumu.</param>
    private static Color StateToColor(MissionState state)
    {
        switch (state)
        {
            case MissionState.Sensing: return ColSensing;
            case MissionState.Waiting: return ColWaiting;
            case MissionState.Cleaning: return ColCleaning;
            default: return ColIdle;
        }
    }

    /// <summary>Duruma karşılık gelen varsayılan operatör yönergesi.</summary>
    /// <param name="state">Görev durumu.</param>
    private static string DefaultInstruction(MissionState state)
    {
        switch (state)
        {
            case MissionState.Idle:
                return "Press START to begin scanning.";
            case MissionState.Sensing:
                return "Scanning in progress. Wait until the robot returns HOME.";
            case MissionState.Waiting:
                return "Review the defect list. Press CONFIRM to start cleaning, "
                       + "or REINSPECT to scan again.";
            case MissionState.Cleaning:
                return "Cleaning in progress. Keep clear of the work area.";
            default:
                return "Waiting for system status...";
        }
    }
}
