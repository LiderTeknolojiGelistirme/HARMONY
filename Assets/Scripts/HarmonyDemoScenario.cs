using System.Collections;
using System.Globalization;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// ROS2 bağlantısı olmadan tüm HARMONY akışını sahte veriyle oynatır.
///
/// Akış ve zamanlama, gerçek bir HIL oturumunun kaydından çıkarıldı
/// (harmony_user_interface/log/harmony_log_20260727_142317.txt ve
/// trajectories/sensing.json + cleaning.json):
///
///   START    → SR_MODE, HOME'a git, wp_0…wp_9 taranır (~84 s),
///              son bakış noktasından sonra HOME'a dönülür (~15 s),
///              dört kusur 0.3 s arayla yayınlanır, WAITING'e geçilir.
///   CONFIRM  → CR_MODE, kusurlar DEF-03 → DEF-02 → DEF-01 → DEF-04
///              sırasıyla temizlenir; her biri önce CLEANING (sarı),
///              bitince CLEANED (yeşil) olur. Sonunda IDLE.
///   REINSPECT→ tabloyu boşaltıp taramayı baştan başlatır.
///   STOP     → akışı keser, bekleyen görevleri ABORTED yapar, IDLE'a döner.
///
/// Veri, ROS'tan gelmiş gibi mevcut abonelerin enjeksiyon kapılarından
/// beslenir (<see cref="HarmonyDefectSubscriber.InjectDefectJson"/> vb.), yani
/// paneller normal akıştaki kodun aynısını çalıştırır. Bu bileşen kapalıyken
/// sahne birebir eskisi gibi ROS'a bağlanır.
/// </summary>
public class HarmonyDemoScenario : MonoBehaviour
{
    // -------------------------------------------------------------------
    // Kayıttan çıkarılan senaryo verisi
    // -------------------------------------------------------------------

    /// <summary>
    /// Tarama bakış noktalarının hareket süreleri [s].
    /// sensing.json içindeki wp_0…wp_9 segmentlerinin son time_from_start
    /// değerleri. Uzunluğu aynı zamanda kaç bakış noktası olduğunu belirler.
    /// </summary>
    private static readonly float[] WaypointDurations =
    {
        6.518f, 7.414f, 7.476f, 4.680f, 3.258f, 5.153f, 11.751f, 1.777f, 16.920f, 3.923f
    };

    /// <summary>
    /// Senaryodaki kusurlar: kimlik, tür, x, y, z (ROS world, metre).
    /// harmony_defects.py ile ve kayıttaki "Defect published" satırlarıyla aynı.
    /// </summary>
    private static readonly string[,] DefectTable =
    {
        { "DEF-01", "Protrusion", "-1.100", "0.800", "1.520" },
        { "DEF-02", "Protrusion", "-1.100", "0.300", "1.530" },
        { "DEF-03", "Dent",       "-0.918", "0.274", "1.040" },
        { "DEF-04", "Dent",       "-0.892", "1.680", "1.000" }
    };

    /// <summary>
    /// Giderim sırası — harmony_defects.py CLEANING_ORDER ve
    /// <see cref="HarmonyGhostTrajectory"/> ile aynı. Lineer eksen hareketini
    /// azaltmak için y'ye göre seçilmiştir, kimlik sırası değildir.
    /// </summary>
    private static readonly string[] CleaningOrder = { "DEF-03", "DEF-02", "DEF-01", "DEF-04" };

    /// <summary>
    /// Her kusurun temizlenme süresi [s], <see cref="CleaningOrder"/> ile aynı
    /// sırada. Kayıttaki CLEANING → CLEANED damgaları arasındaki farklar.
    /// </summary>
    private static readonly float[] CleaningDurations = { 13f, 17f, 7f, 21f };

    /// <summary>Grafiklerde çizilen eklemler; <see cref="HarmonyChartsPanel"/> ile aynı sıra.</summary>
    private static readonly string[] ChartJoints =
    {
        "shoulder_pan_joint", "wrist_3_joint", "elbow_joint", "wrist_2_joint",
        "base_to_robot_mount", "wrist_1_joint", "shoulder_lift_joint"
    };

    /// <summary>
    /// Eklem başına üst hız [rad/s], lineer eksen için [m/s].
    /// <see cref="ChartJoints"/> ile aynı sıra. Kayıtlı yörüngelerin
    /// vel=0.1 ölçek çarpanıyla oynatıldığı UR10e aralığına yakın tutuldu.
    /// </summary>
    private static readonly double[] JointMaxSpeed =
    {
        0.85, 0.90, 0.75, 0.80, 0.30, 0.90, 0.65
    };

    /// <summary>
    /// <see cref="HarmonyCleaningJointPath.ScenarioConfigurations"/> içindeki
    /// [ray, pan, lift, elbow, w1, w2, w3] dizilimini <see cref="ChartJoints"/>
    /// sırasına çeviren indeks tablosu.
    /// </summary>
    private static readonly int[] ConfigToChartIndex = { 1, 6, 3, 5, 0, 4, 2 };

    // -------------------------------------------------------------------
    // Inspector
    // -------------------------------------------------------------------

    [Header("Bağımlılıklar")]
    [Tooltip("Görev durumu hedefi. Boş bırakılırsa sahnede aranır.")]
    [SerializeField] private HarmonyMissionController missionController;

    [Tooltip("Kusur tablosu hedefi. Boş bırakılırsa sahnede aranır.")]
    [SerializeField] private HarmonyDefectSubscriber defectSubscriber;

    [Tooltip("Grafikleri besleyen telemetri. Boş bırakılırsa sahnede aranır.")]
    [SerializeField] private HarmonyTelemetrySubscriber telemetry;

    [Tooltip("Görev listesi paneli. Boş bırakılırsa sahnede aranır.")]
    [SerializeField] private HarmonyTaskList taskList;

    [Header("Komut Butonları")]
    [Tooltip("Boş bırakılırsa sahnede ada göre aranır (BtnStart vb.).")]
    [SerializeField] private Button startButton;
    [SerializeField] private Button confirmButton;
    [SerializeField] private Button reinspectButton;
    [SerializeField] private Button stopButton;

    [Header("Zamanlama")]
    [Tooltip("Tüm süreler bu çarpanla ölçeklenir. 1 = kayıttaki gerçek zaman " +
             "(~2.5 dk). 2 yaparsan iki kat hızlanır.")]
    [Range(0.25f, 8f)]
    [SerializeField] private float speedMultiplier = 1f;

    [Tooltip("START'tan ilk bakış noktasına kadar geçen HOME'a gidiş süresi [s].")]
    [SerializeField] private float goHomeDuration = 1.35f;

    [Tooltip("Her bakış noktasında tarama için beklenen süre [s].")]
    [SerializeField] private float waypointDwell = 1.38f;

    [Tooltip("Son bakış noktasından sonra HOME'a dönüş süresi [s].")]
    [SerializeField] private float returnHomeDuration = 15.0f;

    [Tooltip("Kusurların art arda yayınlanma aralığı [s].")]
    [SerializeField] private float defectPublishInterval = 0.3f;

    [Header("Sahte Telemetri")]
    [Tooltip("Giderim sırasındaki temizleme kuvvetinin ortalaması [N]. " +
             "Kayıttaki /harmony/cleaning_force 9–17 N arasında salınıyordu.")]
    [SerializeField] private float cleaningForceMean = 13f;

    [Tooltip("Temizleme kuvvetinin salınım genliği [N].")]
    [SerializeField] private float cleaningForceAmplitude = 4f;

    [Tooltip("Telemetri örnekleme hızı [Hz].")]
    [Range(5f, 60f)]
    [SerializeField] private float telemetryRateHz = 20f;

    // -------------------------------------------------------------------
    // Durum
    // -------------------------------------------------------------------

    private enum Phase { Idle, Sensing, Waiting, Cleaning }

    private Phase phase = Phase.Idle;
    private Coroutine routine;
    private float nextTelemetryTime;

    // Sahte eklem hareketinin hedefleri; faz değiştikçe yenilenir.
    private readonly double[] jointCurrent = new double[7];
    private readonly double[] jointTarget = new double[7];
    private readonly double[] jointVelocity = new double[7];

    // Giderimde kayıtlı yapılandırma; çevresinde küçük bir taşlama salınımı yapılır.
    private readonly double[] jointBase = new double[7];

    // true ise hedef sabittir (kayıtlı kusur yapılandırması); false ise hedefe
    // varılınca yenisi seçilir, böylece tarama boyunca hareket sürer.
    private bool holdTarget;

    // Kip seçildikten sonra START'a basılana dek hiçbir şey yayınlanmaz;
    // grafikler ve görev listesi boş kalır.
    private bool hasStarted;

    /// <summary>Demo şu anda bir akış oynatıyor mu?</summary>
    public bool IsRunning => routine != null;

    /// <summary>Bakış noktası sayısı — kayıtlı taramada kaç nokta varsa o.</summary>
    public static int WaypointCount => WaypointDurations.Length;

    private void Awake()
    {
        if (missionController == null) missionController = FindObjectOfType<HarmonyMissionController>();
        if (defectSubscriber == null) defectSubscriber = FindObjectOfType<HarmonyDefectSubscriber>();
        if (telemetry == null) telemetry = FindObjectOfType<HarmonyTelemetrySubscriber>();
        if (taskList == null) taskList = FindObjectOfType<HarmonyTaskList>(true);

        if (startButton == null) startButton = FindButton("BtnStart");
        if (confirmButton == null) confirmButton = FindButton("BtnConfirm");
        if (reinspectButton == null) reinspectButton = FindButton("BtnReinspect");
        if (stopButton == null) stopButton = FindButton("BtnStop");
    }

    private void OnEnable()
    {
        // Butonların Inspector'daki kalıcı bağlantıları (ExecCommandViaSocket.Send)
        // yerinde kalır; ROS yokken sessizce uyarı basıp geçerler.
        if (startButton != null) startButton.onClick.AddListener(CommandStart);
        if (confirmButton != null) confirmButton.onClick.AddListener(CommandConfirm);
        if (reinspectButton != null) reinspectButton.onClick.AddListener(CommandReinspect);
        if (stopButton != null) stopButton.onClick.AddListener(CommandStop);

        // Kip seçilir seçilmez akış başlamaz: liste boş, grafikler sessiz.
        // Senaryo yalnızca START SCAN ile başlar.
        hasStarted = false;
        taskList?.Clear();
        PublishStatus("IDLE", "Demo mode ready. Press START SCAN.");
    }

    private void OnDisable()
    {
        if (startButton != null) startButton.onClick.RemoveListener(CommandStart);
        if (confirmButton != null) confirmButton.onClick.RemoveListener(CommandConfirm);
        if (reinspectButton != null) reinspectButton.onClick.RemoveListener(CommandReinspect);
        if (stopButton != null) stopButton.onClick.RemoveListener(CommandStop);

        StopRoutine();
    }

    private static Button FindButton(string name)
    {
        foreach (var b in FindObjectsOfType<Button>(true))
            if (b.gameObject.name == name) return b;
        return null;
    }

    // -------------------------------------------------------------------
    // Komutlar
    // -------------------------------------------------------------------

    /// <summary>START: kusur tablosunu boşaltır ve taramayı başlatır.</summary>
    public void CommandStart()
    {
        StopRoutine();
        defectSubscriber?.Clear();

        hasStarted = true;
        BuildSensingTasks();

        routine = StartCoroutine(SensingRoutine());
    }

    /// <summary>CONFIRM: yalnızca onay beklenirken giderim aşamasını başlatır.</summary>
    public void CommandConfirm()
    {
        if (phase != Phase.Waiting) return;

        StopRoutine();
        routine = StartCoroutine(CleaningRoutine());
    }

    /// <summary>REINSPECT: taramayı baştan başlatır.</summary>
    public void CommandReinspect() => CommandStart();

    /// <summary>STOP: akışı keser, bitmemiş görevleri iptal eder.</summary>
    public void CommandStop()
    {
        StopRoutine();
        phase = Phase.Idle;

        taskList?.SetAllUnfinished(HarmonyTaskState.Aborted);
        PublishStatus("IDLE", "Mission aborted by operator.");
    }

    private void StopRoutine()
    {
        if (routine == null) return;
        StopCoroutine(routine);
        routine = null;
    }

    // -------------------------------------------------------------------
    // Akış
    // -------------------------------------------------------------------

    /// <summary>
    /// Tarama aşaması: HOME → bakış noktaları → HOME → kusurların yayını →
    /// onay bekleme.
    /// </summary>
    private IEnumerator SensingRoutine()
    {
        phase = Phase.Sensing;
        PublishStatus("SR_MODE", "START received, scanning begins");
        RandomizeJointTarget();

        PublishStatus("SR_MODE", "Going HOME");
        yield return Wait(goHomeDuration);

        for (int i = 0; i < WaypointDurations.Length; i++)
        {
            string id = WaypointId(i);
            taskList?.SetState(id, HarmonyTaskState.InProgress);

            PublishStatus("SR_MODE", $"MOVING to waypoint={i}");
            RandomizeJointTarget();
            yield return Wait(WaypointDurations[i]);

            PublishStatus("SR_MODE", $"Scanning viewpoint {i} of {WaypointDurations.Length}");
            yield return Wait(waypointDwell);

            taskList?.SetState(id, HarmonyTaskState.Completed);
        }

        PublishStatus("SR_MODE", "LAST_VIEWPOINT_REACHED — returning HOME");
        SetJointTargetFromConfig(0);   // HOME
        yield return Wait(returnHomeDuration);

        PublishStatus("SR_MODE", "Processing scan data...");

        // Kusurlar kayıttaki gibi kimlik sırasına göre ve art arda yayınlanır.
        for (int i = 0; i < DefectTable.GetLength(0); i++)
        {
            PublishDefect(i);
            yield return Wait(defectPublishInterval);
        }

        // Enjekte edilen gövdeler abonenin Update'inde işleniyor; tabloya
        // girmelerini beklemeden görev listesine bakmak boş döner.
        yield return null;
        AddCleaningTasks();

        phase = Phase.Waiting;
        PublishStatus("WAITING", "Waiting for CONFIRM (or REINSPECT/STOP)");
        routine = null;
    }

    /// <summary>
    /// Giderim aşaması: her kusur sırayla CLEANING → CLEANED yapılır.
    /// </summary>
    private IEnumerator CleaningRoutine()
    {
        phase = Phase.Cleaning;
        PublishStatus("CR_MODE", "CONFIRM received, cleaning begins");

        for (int i = 0; i < CleaningOrder.Length; i++)
        {
            string id = CleaningOrder[i];

            PublishDefectStatus(id, "CLEANING");
            taskList?.SetState(id, HarmonyTaskState.InProgress);
            PublishStatus("CR_MODE", $"Cleaning {id} ({i + 1}/{CleaningOrder.Length})");

            // Kayıtlı eklem yapılandırmasına git; ScenarioConfigurations sırası
            // [HOME, DEF-03, DEF-02, DEF-01, DEF-04, HOME] olduğu için +1.
            SetJointTargetFromConfig(i + 1);

            float duration = i < CleaningDurations.Length ? CleaningDurations[i] : 10f;
            yield return Wait(duration);

            PublishDefectStatus(id, "CLEANED");
            taskList?.SetState(id, HarmonyTaskState.Completed);
        }

        PublishStatus("CR_MODE", "All defects cleaned, returning HOME");
        SetJointTargetFromConfig(0);
        yield return Wait(returnHomeDuration * 0.5f);

        phase = Phase.Idle;
        PublishStatus("IDLE", "Mission complete. All defects cleaned.");
        routine = null;
    }

    /// <summary>Süreyi hız çarpanına bölerek bekler.</summary>
    /// <param name="seconds">Kayıttaki gerçek süre [s].</param>
    private WaitForSeconds Wait(float seconds)
        => new WaitForSeconds(seconds / Mathf.Max(speedMultiplier, 0.01f));

    // -------------------------------------------------------------------
    // Sahte veri yayını — abonelerin ROS kapılarından girer
    // -------------------------------------------------------------------

    /// <summary>
    /// Görev durumunu ROS'tan gelmiş gibi besler.
    /// </summary>
    /// <param name="state">IDLE, SR_MODE, WAITING veya CR_MODE.</param>
    /// <param name="note">Operatöre gösterilecek açıklama.</param>
    private void PublishStatus(string state, string note)
    {
        missionController?.InjectStatusJson(
            "{\"state\":\"" + state + "\",\"note\":\"" + note.Replace("\"", "'") + "\"}");
    }

    /// <summary>
    /// Tablodaki bir kusuru ROS gövdesi biçiminde yayınlar.
    /// </summary>
    /// <param name="index">DefectTable satır sırası.</param>
    private void PublishDefect(int index)
    {
        string json =
            "{\"defect_id\":\"" + DefectTable[index, 0] + "\"," +
            "\"defect_type\":\"" + DefectTable[index, 1] + "\"," +
            "\"frame_id\":\"world\"," +
            "\"position\":{\"x\":" + DefectTable[index, 2] +
                        ",\"y\":" + DefectTable[index, 3] +
                        ",\"z\":" + DefectTable[index, 4] + "}," +
            "\"approach\":{\"x\":0.0,\"y\":0.0,\"z\":0.0}," +
            "\"status\":\"DETECTED\"}";

        defectSubscriber?.InjectDefectJson(json);
    }

    /// <summary>
    /// Kusur durumu güncellemesi yayınlar.
    /// </summary>
    /// <param name="defectId">Kusur kimliği.</param>
    /// <param name="status">DETECTED, CLEANING veya CLEANED.</param>
    private void PublishDefectStatus(string defectId, string status)
    {
        defectSubscriber?.InjectStatusJson(
            "{\"defect_id\":\"" + defectId + "\",\"status\":\"" + status + "\"}");
    }

    // -------------------------------------------------------------------
    // Görev listesi
    // -------------------------------------------------------------------

    /// <summary>Bakış noktasının görev kimliği, örn. "WP-03".</summary>
    /// <param name="index">Sıfır tabanlı bakış noktası sırası.</param>
    private static string WaypointId(int index)
        => "WP-" + index.ToString("00", CultureInfo.InvariantCulture);

    /// <summary>
    /// Görev listesini yalnızca tarama noktalarıyla kurar. Giderim görevleri
    /// kusurlar tespit edilene kadar listeye girmez; henüz var olup
    /// olmadıkları bilinmiyor.
    /// </summary>
    private void BuildSensingTasks()
    {
        if (taskList == null) return;

        taskList.Clear();

        for (int i = 0; i < WaypointDurations.Length; i++)
            taskList.AddTask(WaypointId(i), $"Scan viewpoint {i}");
    }

    /// <summary>
    /// Tespit edilen kusurları görev listesine ekler. Sıra giderim sırasıdır
    /// (<see cref="CleaningOrder"/>), yani listedeki sıralama işlerin
    /// yapılacağı sıradır — tespit sırası değil.
    /// </summary>
    private void AddCleaningTasks()
    {
        if (taskList == null || defectSubscriber == null) return;

        for (int i = 0; i < CleaningOrder.Length; i++)
        {
            HarmonyDefect defect = defectSubscriber.GetDefect(CleaningOrder[i]);
            if (defect == null) continue;

            taskList.AddTask(CleaningOrder[i], "Clean " + defect.DefectType.ToLowerInvariant());
        }
    }

    // -------------------------------------------------------------------
    // Sahte telemetri — grafikleri canlı tutar
    // -------------------------------------------------------------------

    private void Update()
    {
        if (telemetry == null) return;

        // START'a basılmadan grafikler hareket etmesin; aksi halde kip seçer
        // seçmez senaryo başlamış gibi görünüyor.
        if (!hasStarted) return;

        if (Time.time < nextTelemetryTime) return;
        nextTelemetryTime = Time.time + 1f / Mathf.Max(telemetryRateHz, 1f);

        // Giderimde kol kusurun üzerinde durur ama temizlik yapar; küçük bir
        // salınım eklenmezse grafikler düz çizgiye döner.
        if (holdTarget && phase == Phase.Cleaning)
        {
            for (int i = 0; i < jointTarget.Length; i++)
                jointTarget[i] = jointBase[i] + Mathf.Sin(Time.time * (2.1f + i * 0.37f)) * 0.045;
        }

        StepJoints();

        for (int i = 0; i < ChartJoints.Length; i++)
            telemetry.InjectJoint(ChartJoints[i], jointCurrent[i], jointVelocity[i]);

        telemetry.InjectForce(CurrentForce());
    }

    /// <summary>
    /// Eklem açılarını hedefe doğru hız sınırlı olarak ilerletir; hızlar
    /// gerçek adımdan türetilir.
    ///
    /// Üstel yumuşatma yerine hız sınırı kullanılıyor: üstel yaklaşımda adım
    /// başına kat edilen yol hedefe olan uzaklıkla orantılı olduğu için uzak
    /// hedeflerde eklem hızı onlarca rad/s'ye çıkıyor ve hız grafiği anlamsız
    /// oluyordu. Burada hızlar UR10e'nin makul çalışma aralığında kalır.
    ///
    /// Hız sınırı bilerek <see cref="speedMultiplier"/> ile ölçeklenmez;
    /// hızlandırılmış demoda eklemler hedefe yetişemez ama grafik gerçekçi kalır.
    /// </summary>
    private void StepJoints()
    {
        double dt = 1.0 / Mathf.Max(telemetryRateHz, 1f);

        for (int i = 0; i < jointCurrent.Length; i++)
        {
            double diff = jointTarget[i] - jointCurrent[i];
            double maxStep = JointMaxSpeed[i] * dt;

            // Hedefe yaklaşırken yavaşla; yoksa sabit hızdan sıfıra sıçrayıp
            // hız grafiğinde dik basamak oluşuyor.
            double ease = System.Math.Min(1.0, System.Math.Abs(diff) / 0.35);
            maxStep *= System.Math.Max(ease, 0.05);

            double step = System.Math.Max(-maxStep, System.Math.Min(maxStep, diff));

            jointCurrent[i] += step;
            jointVelocity[i] = step / dt;
        }

        // Tarama sırasında hedefe varıldıysa yenisini seç; aksi halde kol
        // bakış noktasının geri kalanında hareketsiz kalır.
        if (!holdTarget && IsAtTarget()) RandomizeJointTarget();
    }

    /// <summary>Tüm eklemler hedeflerine yeterince yaklaştı mı?</summary>
    private bool IsAtTarget()
    {
        for (int i = 0; i < jointCurrent.Length; i++)
            if (System.Math.Abs(jointTarget[i] - jointCurrent[i]) > 0.02) return false;

        return true;
    }

    /// <summary>
    /// Giderimde kayıttaki aralıkta salınan kuvvet, diğer fazlarda sıfıra
    /// yakın gürültü üretir.
    /// </summary>
    private double CurrentForce()
    {
        if (phase != Phase.Cleaning)
            return Mathf.PerlinNoise(Time.time * 1.3f, 7.1f) * 0.4f;

        float t = Time.time;
        float wave = Mathf.Sin(t * 5.7f) * 0.6f + Mathf.Sin(t * 13.1f) * 0.4f;
        return cleaningForceMean + wave * cleaningForceAmplitude;
    }

    /// <summary>
    /// Eklem hedefini kayıtlı senaryo yapılandırmalarından birine alır.
    /// </summary>
    /// <param name="configIndex">
    /// <see cref="HarmonyCleaningJointPath.ScenarioConfigurations"/> sırası:
    /// 0=HOME, 1=DEF-03, 2=DEF-02, 3=DEF-01, 4=DEF-04, 5=HOME.
    /// </param>
    private void SetJointTargetFromConfig(int configIndex)
    {
        var configs = HarmonyCleaningJointPath.ScenarioConfigurations;
        if (configIndex < 0 || configIndex >= configs.Length) return;

        double[] cfg = configs[configIndex];
        for (int i = 0; i < ConfigToChartIndex.Length && i < jointTarget.Length; i++)
        {
            jointBase[i] = cfg[ConfigToChartIndex[i]];
            jointTarget[i] = jointBase[i];
        }

        holdTarget = true;
    }

    /// <summary>
    /// Tarama için makul bir eklem hedefi üretir.
    ///
    /// Giderim aşamasının aksine taramanın kayıtlı eklem açıları projeye
    /// alınmadı (sensing.json ROS deposunda, 866 KB). Buradaki değerler
    /// gerçek değil, yalnızca grafiklerin canlı görünmesi içindir.
    /// </summary>
    private void RandomizeJointTarget()
    {
        // HOME çevresinde, eklem limitleri içinde kalan salınımlar.
        jointTarget[0] = Random.Range(-2.6f, 2.6f);    // shoulder pan
        jointTarget[1] = Random.Range(-2.5f, 2.5f);    // wrist 3
        jointTarget[2] = Random.Range(-1.4f, 0.6f);    // elbow
        jointTarget[3] = Random.Range(-1.9f, -1.2f);   // wrist 2
        jointTarget[4] = Random.Range(0.1f, 1.9f);     // lineer eksen
        jointTarget[5] = Random.Range(-1.8f, 1.8f);    // wrist 1
        jointTarget[6] = Random.Range(-2.1f, -0.9f);   // shoulder lift

        holdTarget = false;
    }
}
