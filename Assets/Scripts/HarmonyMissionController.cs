using System;
using System.Collections.Concurrent;
using Newtonsoft.Json;
using RosSharp.RosBridgeClient;
using UnityEngine;
using Zenject;

// System.String ile ROS# std_msgs/String aynı adı taşıdığı için takma ad kullanılır.
using RosString = RosSharp.RosBridgeClient.MessageTypes.Std.String;

/// <summary>
/// HARMONY görev akışının durumu.
/// ROS tarafındaki /harmony/robot_status içindeki "state" alanıyla eşleşir.
/// </summary>
public enum MissionState
{
    /// <summary>Görev başlatılmadı.</summary>
    Idle,

    /// <summary>Operatör onayı bekleniyor.</summary>
    Waiting,

    /// <summary>Sensing robotu kapıyı tarıyor (SR_MODE).</summary>
    Sensing,

    /// <summary>Cleaning robotu kusurları gideriyor (CR_MODE).</summary>
    Cleaning,

    /// <summary>Tanınmayan bir durum metni geldi.</summary>
    Unknown
}

/// <summary>
/// AR arayüzünün görev katmanı: operatör komutlarını /harmony/cmd_input
/// topic'ine yayınlar ve /harmony/robot_status'tan gelen durumu ayrıştırıp
/// panellere olay olarak dağıtır.
///
/// Komut gönderimi <see cref="ExecCommandViaSocket"/> üzerinden yapılır; bu
/// sınıf yalnızca doğru JSON gövdesini üretmekten sorumludur.
/// </summary>
public class HarmonyMissionController : MonoBehaviour
{
    // ROS tarafının kabul ettiği komutlar (harmony_user_interface/app.py: VALID_CMDS).
    private const string CmdStart = "START";
    private const string CmdConfirm = "CONFIRM";
    private const string CmdReinspect = "REINSPECT";
    private const string CmdStop = "STOP";

    [Inject] private RosConnector rosConnector;
    [Inject] private ExecCommandViaSocket commandPublisher;

    [Header("Bağımlılıklar")]
    [Tooltip("Kusur tablosu. Boş bırakılırsa sahnede aranır.")]
    [SerializeField] private HarmonyDefectSubscriber defectSubscriber;

    [Header("Topic Ayarları")]
    [Tooltip("Görev durumu yayını (std_msgs/String).")]
    [SerializeField] private string robotStatusTopic = "/harmony/robot_status";

    [Header("Davranış")]
    [Tooltip("START komutunda kusur tablosu otomatik temizlensin mi? " +
             "Web arayüzü de aynısını yapar.")]
    [SerializeField] private bool clearDefectsOnStart = true;

    [Header("Hata Ayıklama")]
    [SerializeField] private bool verboseLogging = false;

    // ROS# geri çağrısı arka thread'den gelir; ham gövde kuyruğa alınır.
    private readonly ConcurrentQueue<string> statusQueue = new ConcurrentQueue<string>();

    /// <summary>Görev durumu değiştiğinde tetiklenir (durum, açıklama notu).</summary>
    public event Action<MissionState, string> OnMissionStateChanged;

    /// <summary>Bir komut ROS'a yayınlandığında tetiklenir.</summary>
    public event Action<string> OnCommandSent;

    /// <summary>Güncel görev durumu.</summary>
    public MissionState State { get; private set; } = MissionState.Idle;

    /// <summary>ROS'un gönderdiği serbest metin açıklama; operatör yönergesi olarak kullanılabilir.</summary>
    public string Note { get; private set; } = string.Empty;

    /// <summary>
    /// rosbridge soketi ayakta mı? Panellerdeki bağlantı göstergesi bunu kullanır.
    /// Görev durumu mesajı gelip gelmediğinden bağımsızdır.
    /// </summary>
    public bool IsRosConnected =>
        rosConnector != null && rosConnector.RosSocket != null;

    /// <summary>
    /// Demo kipinde mi çalışıyoruz? <see cref="HarmonyModeSelector"/> tarafından
    /// açılır. Açıkken komutlar ROS'a yayınlanmaz; durum ve kusurlar
    /// <see cref="HarmonyDemoScenario"/>'nun enjekte ettiği sahte veriden gelir.
    /// </summary>
    public bool DemoMode { get; set; }

    private void Start()
    {
        if (defectSubscriber == null)
            defectSubscriber = FindObjectOfType<HarmonyDefectSubscriber>();

        if (commandPublisher == null)
        {
            commandPublisher = FindObjectOfType<ExecCommandViaSocket>();
            if (commandPublisher == null)
                Debug.LogError("HarmonyMissionController: ExecCommandViaSocket bulunamadı! " +
                               "Komutlar gönderilemeyecek.");
        }

        if (rosConnector?.RosSocket == null)
        {
            Debug.LogError("HarmonyMissionController: RosSocket null! Durum dinlenemeyecek.");
            return;
        }

        rosConnector.RosSocket.Subscribe<RosString>(robotStatusTopic, ReceiveStatusMessage);
        Debug.Log($"HarmonyMissionController: abone olundu → {robotStatusTopic}");
    }

    // ---------------------------------------------------------------------
    // Komutlar — UI butonlarına doğrudan bağlanabilir
    // ---------------------------------------------------------------------

    /// <summary>Yeni tarama turunu başlatır. Kusur tablosunu temizler.</summary>
    public void SendStart()
    {
        if (clearDefectsOnStart)
            defectSubscriber?.Clear();

        PublishCommand(CmdStart);
    }

    /// <summary>Tarama sonrası temizleme aşamasına geçilmesini onaylar.</summary>
    public void SendConfirm() => PublishCommand(CmdConfirm);

    /// <summary>Yüzeyin yeniden taranmasını ister.</summary>
    public void SendReinspect() => PublishCommand(CmdReinspect);

    /// <summary>Görevi durdurur.</summary>
    public void SendStop() => PublishCommand(CmdStop);

    /// <summary>
    /// Komutu ROS'un beklediği JSON gövdesine sarıp yayınlar.
    /// </summary>
    /// <param name="cmd">START, CONFIRM, REINSPECT veya STOP.</param>
    private void PublishCommand(string cmd)
    {
        // Demo kipinde ROS yok; komut yalnızca panellere olay olarak duyurulur.
        if (DemoMode)
        {
            OnCommandSent?.Invoke(cmd);
            return;
        }

        if (commandPublisher == null)
        {
            Debug.LogWarning($"HarmonyMissionController: komut yayınlanamadı ({cmd}) — publisher yok.");
            return;
        }

        string payload = JsonConvert.SerializeObject(new HarmonyCommandDto { Cmd = cmd });
        commandPublisher.Send(payload);

        if (verboseLogging)
            Debug.Log($"HarmonyMissionController: komut gönderildi → {payload}");

        OnCommandSent?.Invoke(cmd);
    }

    // ---------------------------------------------------------------------
    // Durum dinleme
    // ---------------------------------------------------------------------

    private void ReceiveStatusMessage(RosString message)
    {
        if (message?.data == null) return;
        statusQueue.Enqueue(message.data);
    }

    private void Update()
    {
        while (statusQueue.TryDequeue(out string json))
            ProcessStatusJson(json);
    }

    /// <summary>
    /// Test/simülasyon amaçlı: ROS'tan gelmiş gibi görev durumu besler.
    /// </summary>
    /// <param name="json">Durum JSON'u, örn. {"state":"SR_MODE","note":"..."}.</param>
    public void InjectStatusJson(string json)
    {
        if (!string.IsNullOrEmpty(json)) statusQueue.Enqueue(json);
    }

    /// <summary>
    /// Durum gövdesini ayrıştırır ve değişiklik varsa olayı tetikler.
    /// </summary>
    /// <param name="json">Ham JSON metni.</param>
    private void ProcessStatusJson(string json)
    {
        try
        {
            var dto = JsonConvert.DeserializeObject<HarmonyRobotStatusDto>(json);
            if (dto == null) return;

            MissionState newState = ParseState(dto.State);
            string newNote = dto.Note ?? string.Empty;

            if (newState == State && newNote == Note) return;

            State = newState;
            Note = newNote;

            if (verboseLogging)
                Debug.Log($"HarmonyMissionController: durum → {State} | {Note}");

            OnMissionStateChanged?.Invoke(State, Note);
        }
        catch (JsonException e)
        {
            Debug.LogWarning($"HarmonyMissionController: durum JSON'u ayrıştırılamadı — {e.Message}");
        }
    }

    /// <summary>
    /// ROS durum metnini enum'a çevirir.
    /// </summary>
    /// <param name="raw">Ham metin, örn. "SR_MODE".</param>
    public static MissionState ParseState(string raw)
    {
        if (string.IsNullOrEmpty(raw))
            return MissionState.Unknown;

        switch (raw.Trim().ToUpperInvariant())
        {
            case "IDLE": return MissionState.Idle;
            case "WAITING": return MissionState.Waiting;
            case "SR_MODE": return MissionState.Sensing;
            case "CR_MODE": return MissionState.Cleaning;
            default: return MissionState.Unknown;
        }
    }

    /// <summary>
    /// Durumun operatöre gösterilecek metni. Arayüz dili İngilizcedir.
    /// </summary>
    /// <param name="state">Görev durumu.</param>
    public static string StateToDisplayText(MissionState state)
    {
        switch (state)
        {
            case MissionState.Idle: return "Idle";
            case MissionState.Waiting: return "Awaiting Confirmation";
            case MissionState.Sensing: return "Sensing";
            case MissionState.Cleaning: return "Cleaning";
            default: return "Unknown";
        }
    }
}
