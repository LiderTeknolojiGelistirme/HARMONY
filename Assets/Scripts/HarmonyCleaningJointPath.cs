using System.Collections.Concurrent;
using System.Collections.Generic;
using Newtonsoft.Json;
using RosSharp.RosBridgeClient;
using UnityEngine;
using Zenject;

using RosString = RosSharp.RosBridgeClient.MessageTypes.Std.String;

/// <summary>
/// Giderim (cleaning) senaryosunda robotun izleyeceği TCP yolunu eklem
/// uzayından üretir.
///
/// Kusur konumlarını birleştirmek yerine robotun gerçekten geçtiği eklem
/// açıları alınır ve her ara adımda <see cref="HarmonyRobotKinematics"/> ile
/// ileri kinematik çözülür. Böylece hayalet çizgi, kolun kusurlar arasında
/// çizdiği eğriyi de gösterir.
///
/// Kaynak önceliği:
///   1. ROS topic'i (<see cref="jointPathTopic"/>) — çalışma anında bir yol
///      yayınlanırsa o kullanılır.
///   2. Inspector'a atanan <see cref="recordedPath"/> TextAsset'i.
///   3. Resources altındaki <see cref="resourcePath"/> dosyası (varsayılan).
///   4. Hiçbiri yoksa <see cref="ScenarioConfigurations"/> tablosu arasında
///      lineer interpolasyon.
///
/// 1–3 aynı JSON şemasını kullanır; ROS tarafı yayın yapmak isterse
/// Tools/generate_ghost_joint_path.py çıktısını olduğu gibi yayınlaması yeter.
///
/// DİKKAT — 4. seçenek kabadır. Kayıtlı MoveIt yörüngesiyle karşılaştırıldığında
/// lineer interpolasyon TCP yolundan ortalama 91 mm, en kötü 337 mm sapıyor
/// (ölçüm: 03.08.2026). Örnek sayısını artırmak bunu düzeltmez; sapma
/// örnekleme sıklığından değil, planlayıcının eklem uzayında düz gitmemesinden
/// kaynaklanıyor. Yalnız kayıtlı yol yokken kullanılmalıdır.
/// </summary>
public class HarmonyCleaningJointPath : MonoBehaviour
{
    /// <summary>
    /// Yedek (interpolasyon) yolunun uğradığı yapılandırmalar.
    /// harmony_defects.py içindeki HOME_JOINTS ve DEFECT_JOINTS ile birebir
    /// aynı olmalıdır; sıra CLEANING_ORDER'dır ve görev sonunda HOME'a dönülür.
    /// Değerler [ray(m), pan, lift, elbow, w1, w2, w3].
    /// </summary>
    public static readonly double[][] ScenarioConfigurations =
    {
        new[] { 1.0, 0.0, -HalfPi, 0.0, -HalfPi, 0.0, 0.0 },                               // HOME
        new[] { 0.472524, 0.705916, -2.004140, -1.118372, 1.551177, -1.569809, 0.707527 }, // DEF-03
        new[] { 0.147541, 2.909280, -1.012262, 0.040188, -0.661241, -1.549926, 2.916590 }, // DEF-02
        new[] { 0.645241, 2.907982, -1.009268, 0.044431, -0.662556, -1.551967, 2.914284 }, // DEF-01
        new[] { 1.891474, 0.744085, -1.975678, -1.229000, 1.633109, -1.569839, 0.745605 }, // DEF-04
        new[] { 1.0, 0.0, -HalfPi, 0.0, -HalfPi, 0.0, 0.0 },                               // HOME
    };

    /// <summary>HOME yapılandırmasındaki -pi/2 değerleri (Python tarafıyla aynı).</summary>
    private const double HalfPi = System.Math.PI / 2.0;

    [Header("Kaynaklar")]
    [Tooltip("İleri kinematik. Boş bırakılırsa sahnede aranır.")]
    [SerializeField] private HarmonyRobotKinematics kinematics;

    [Header("Kayıtlı Yörünge")]
    [Tooltip("Tools/generate_ghost_joint_path.py çıktısı. Boşsa Resources'tan okunur.")]
    [SerializeField] private TextAsset recordedPath;

    [Tooltip("Resources altındaki yol (uzantısız).")]
    [SerializeField] private string resourcePath = "Harmony/harmony_cleaning_joint_path";

    [Header("Topic")]
    [Tooltip("Çalışma anında eklem yolu yayını (std_msgs/String, aynı JSON şeması). " +
             "Boş bırakılırsa abone olunmaz.")]
    [SerializeField] private string jointPathTopic = "/harmony/cleaning_joint_path";

    [Header("Örnekleme")]
    [Tooltip("Kayıtlı yolda kaç noktada bir örnek alınacağı. 1 = tümü. " +
             "4'te kalan sapma 5.7 mm, 8'de 17.2 mm (03.08.2026 ölçümü).")]
    [SerializeField] private int stride = 2;

    [Tooltip("Kayıtlı yol yokken her segmentte üretilecek ara adım sayısı.")]
    [SerializeField] private int interpolationSamples = 32;

    [Header("Hata Ayıklama")]
    [SerializeField] private bool verboseLogging;

    [Inject] private RosConnector rosConnector;

    /// <summary>ROS world çerçevesinde TCP yol noktaları.</summary>
    private readonly List<Vector3> rosPoints = new List<Vector3>();

    /// <summary>Eklem yapılandırmaları; TCP'ye çevrilmeden önceki hâl.</summary>
    private readonly List<double[]> configurations = new List<double[]>();

    // ROS geri çağrısı arka thread'den gelir.
    private readonly ConcurrentQueue<string> pathQueue = new ConcurrentQueue<string>();

    private bool rebuildRequested = true;

    /// <summary>
    /// ROS world çerçevesinde TCP yolu. Yol kurulamadıysa boş liste döner.
    /// </summary>
    public IReadOnlyList<Vector3> RosPoints
    {
        get
        {
            if (rebuildRequested) Rebuild();
            return rosPoints;
        }
    }

    /// <summary>
    /// Yol, robotun gerçekten oynattığı kayıttan mı geliyor? False ise
    /// interpolasyon yedeği kullanılıyordur ve yol kabadır.
    /// </summary>
    public bool IsRecorded { get; private set; }

    private void Start()
    {
        if (kinematics == null) kinematics = FindObjectOfType<HarmonyRobotKinematics>();

        if (kinematics == null)
        {
            Debug.LogWarning("HarmonyCleaningJointPath: HarmonyRobotKinematics bulunamadı; " +
                             "eklem uzayı yolu üretilemeyecek.");
            return;
        }

        LoadRecordedConfigurations();

        if (!string.IsNullOrEmpty(jointPathTopic) && rosConnector?.RosSocket != null)
        {
            rosConnector.RosSocket.Subscribe<RosString>(jointPathTopic, ReceiveJointPath);
            if (verboseLogging)
                Debug.Log("HarmonyCleaningJointPath: abone olundu → " + jointPathTopic);
        }
    }

    private void Update()
    {
        string json;
        bool received = false;

        // Kuyrukta birden fazla varsa yalnız sonuncusu geçerlidir.
        string latest = null;
        while (pathQueue.TryDequeue(out json))
        {
            latest = json;
            received = true;
        }

        if (!received) return;

        if (TryParseConfigurations(latest, "ROS topic"))
        {
            IsRecorded = true;
            rebuildRequested = true;
        }
    }

    private void ReceiveJointPath(RosString msg)
    {
        if (msg == null || string.IsNullOrEmpty(msg.data)) return;
        pathQueue.Enqueue(msg.data);
    }

    /// <summary>
    /// Inspector'a atanan ya da Resources altındaki kaydı okur; ikisi de yoksa
    /// senaryo tablosundan interpolasyonla yol kurar.
    /// </summary>
    private void LoadRecordedConfigurations()
    {
        TextAsset asset = recordedPath;

        if (asset == null && !string.IsNullOrEmpty(resourcePath))
            asset = Resources.Load<TextAsset>(resourcePath);

        if (asset != null && TryParseConfigurations(asset.text, asset.name))
        {
            IsRecorded = true;
            rebuildRequested = true;
            return;
        }

        Debug.LogWarning("HarmonyCleaningJointPath: kayıtlı yörünge " +
                         (asset == null ? "bulunamadı (Resources yolu: " + resourcePath + ")"
                                        : "okunamadı (" + asset.name + ")") + ". " +
                         "Senaryo tablosundan lineer interpolasyona düşülüyor; " +
                         "bu yol gerçek harekete göre 30 cm'e kadar sapabilir.");

        BuildInterpolatedConfigurations();
        IsRecorded = false;
        rebuildRequested = true;
    }

    /// <summary>
    /// JSON gövdesini eklem yapılandırmalarına çevirir.
    /// </summary>
    /// <param name="json">Tools/generate_ghost_joint_path.py şemasındaki gövde.</param>
    /// <param name="sourceName">Günlük mesajlarında geçecek kaynak adı.</param>
    /// <returns>En az iki nokta okunabildiyse true.</returns>
    private bool TryParseConfigurations(string json, string sourceName)
    {
        HarmonyJointPathDto dto;

        try
        {
            dto = JsonConvert.DeserializeObject<HarmonyJointPathDto>(json);
        }
        catch (JsonException e)
        {
            Debug.LogWarning("HarmonyCleaningJointPath: " + sourceName +
                             " çözümlenemedi — " + e.Message);
            return false;
        }

        if (dto?.Segments == null || dto.Segments.Length == 0)
        {
            Debug.LogWarning("HarmonyCleaningJointPath: " + sourceName + " içinde segment yok.");
            return false;
        }

        var parsed = new List<double[]>();

        for (int s = 0; s < dto.Segments.Length; s++)
        {
            var segment = dto.Segments[s];
            if (segment?.Positions == null) continue;

            for (int p = 0; p < segment.Positions.Length; p++)
            {
                double[] configuration = segment.Positions[p];

                if (configuration == null || configuration.Length < HarmonyRobotKinematics.ConfigurationLength)
                {
                    Debug.LogWarning("HarmonyCleaningJointPath: " + sourceName + " içinde " +
                                     (segment.Key ?? "?") + " segmentinin " + p +
                                     ". noktası 7 eklem değeri taşımıyor, atlanıyor.");
                    continue;
                }

                // Segment sınırlarında aynı yapılandırma iki kez geçer
                // (önceki segmentin sonu = sonrakinin başı); çizgide çift
                // nokta olmasın.
                if (parsed.Count > 0 && SameConfiguration(parsed[parsed.Count - 1], configuration))
                    continue;

                parsed.Add(configuration);
            }
        }

        if (parsed.Count < 2)
        {
            Debug.LogWarning("HarmonyCleaningJointPath: " + sourceName +
                             " içinde yeterli nokta yok (" + parsed.Count + ").");
            return false;
        }

        configurations.Clear();
        configurations.AddRange(parsed);

        if (verboseLogging)
            Debug.Log("HarmonyCleaningJointPath: " + sourceName + " → " +
                      configurations.Count + " eklem noktası.");

        return true;
    }

    /// <summary>
    /// Senaryo tablosundaki yapılandırmalar arasında lineer interpolasyonla
    /// yedek yol kurar.
    /// </summary>
    private void BuildInterpolatedConfigurations()
    {
        configurations.Clear();

        int samples = Mathf.Max(2, interpolationSamples);

        for (int w = 0; w < ScenarioConfigurations.Length - 1; w++)
        {
            double[] from = ScenarioConfigurations[w];
            double[] to = ScenarioConfigurations[w + 1];

            // Segment başlangıcı bir öncekinin sonuyla aynı; ilk segment
            // dışında atlanır.
            int first = (w == 0) ? 0 : 1;

            for (int i = first; i < samples; i++)
            {
                double t = i / (double)(samples - 1);
                var configuration = new double[HarmonyRobotKinematics.ConfigurationLength];

                for (int j = 0; j < configuration.Length; j++)
                    configuration[j] = from[j] + t * (to[j] - from[j]);

                configurations.Add(configuration);
            }
        }
    }

    /// <summary>
    /// Eklem yapılandırmalarını ileri kinematikle TCP noktalarına çevirir.
    /// </summary>
    private void Rebuild()
    {
        rebuildRequested = false;
        rosPoints.Clear();

        if (kinematics == null || configurations.Count == 0) return;

        int step = Mathf.Max(1, stride);

        for (int i = 0; i < configurations.Count; i += step)
            rosPoints.Add(kinematics.TcpInRosWorld(configurations[i]));

        // Seyreltme son noktayı atlamış olabilir; yol eksik bitmesin.
        int last = configurations.Count - 1;
        if (last % step != 0)
            rosPoints.Add(kinematics.TcpInRosWorld(configurations[last]));

        if (verboseLogging)
            Debug.Log("HarmonyCleaningJointPath: " + rosPoints.Count + " TCP noktası kuruldu " +
                      (IsRecorded ? "(kayıtlı yörünge)." : "(interpolasyon yedeği)."));
    }

    /// <summary>
    /// Takım ötelemesi ya da hücre kalibrasyonu değiştiğinde TCP noktalarının
    /// yeniden hesaplanmasını ister.
    /// </summary>
    public void Invalidate()
    {
        rebuildRequested = true;
    }

    /// <summary>
    /// İki eklem yapılandırması pratikte aynı mı?
    /// </summary>
    /// <param name="a">Birinci yapılandırma.</param>
    /// <param name="b">İkinci yapılandırma.</param>
    private static bool SameConfiguration(double[] a, double[] b)
    {
        for (int i = 0; i < HarmonyRobotKinematics.ConfigurationLength; i++)
        {
            // Kayıtta değerler 1e-6'ya yuvarlı; eşik onun biraz üstünde.
            if (System.Math.Abs(a[i] - b[i]) > 1e-5) return false;
        }
        return true;
    }

#if UNITY_EDITOR
    private void OnValidate()
    {
        rebuildRequested = true;
    }
#endif
}

/// <summary>
/// Tools/generate_ghost_joint_path.py çıktısının gövdesi.
/// </summary>
public class HarmonyJointPathDto
{
    [JsonProperty("joint_names")] public string[] JointNames;
    [JsonProperty("segments")] public HarmonyJointPathSegmentDto[] Segments;
}

/// <summary>
/// Tek bir hareket segmenti (bir kusura gidiş ya da HOME dönüşü).
/// </summary>
public class HarmonyJointPathSegmentDto
{
    /// <summary>Segment anahtarı: kusur kimliği ya da "home_end".</summary>
    [JsonProperty("key")] public string Key;

    /// <summary>Nokta başına [ray, pan, lift, elbow, w1, w2, w3].</summary>
    [JsonProperty("positions")] public double[][] Positions;
}
