using System.Collections.Concurrent;
using System.Collections.Generic;
using RosSharp.RosBridgeClient;
using UnityEngine;
using Zenject;

using RosPath = RosSharp.RosBridgeClient.MessageTypes.Nav.Path;

/// <summary>
/// Robotun uç noktasının izleyeceği yolu AR sahnesinde hayalet çizgi olarak
/// gösterir.
///
/// İki kaynak kullanılır:
///   * Tarama (sensing) — ROS'un yayınladığı /harmony/path_plan (nav_msgs/Path).
///     Sensing node'u her hareket segmentinde başlangıç ve bitiş arasını
///     örnekleyip yayınlar, yani gerçek plan izlenir.
///   * Giderim (cleaning) — cleaning node'u yol yayınlamaz, ancak hareket
///     tamamen belirlenimlidir: hedef sırası harmony_defects.CLEANING_ORDER
///     ile sabittir ve her kusur için sabit eklem açıları kullanılır.
///     <see cref="HarmonyCleaningJointPath"/> bu eklem açılarından ileri
///     kinematikle gerçek TCP yolunu üretir. O bileşen sahnede yoksa yol,
///     eskisi gibi kusur konumlarından kurulur — kolun kusurlar arasında
///     çizdiği eğriyi göstermeyen kaba bir yaklaşım.
///
/// ROS world koordinatları <see cref="HarmonyDefectSurfaceMapper"/> üzerinden
/// AR sahnesine taşınır; böylece hayalet çizgi kusur işaretçileriyle aynı
/// uzayda kalır.
/// </summary>
[RequireComponent(typeof(LineRenderer))]
public class HarmonyGhostTrajectory : MonoBehaviour
{
    /// <summary>
    /// Giderim sırası — harmony_defects.py içindeki CLEANING_ORDER ile birebir
    /// aynı olmalıdır. Sıra lineer eksen hareketini azaltmak için y'ye göre
    /// artan seçilmiştir.
    /// </summary>
    private static readonly string[] CleaningOrder = { "DEF-03", "DEF-02", "DEF-01", "DEF-04" };

    [Inject] private RosConnector rosConnector;

    [Header("Kaynaklar")]
    [Tooltip("ROS→AR dönüşümü. Boş bırakılırsa sahnede aranır.")]
    [SerializeField] private HarmonyDefectSurfaceMapper mapper;

    [Tooltip("Kusur listesi. Boş bırakılırsa sahnede aranır.")]
    [SerializeField] private HarmonyDefectSubscriber defectSubscriber;

    [Tooltip("Görev durumu. Boş bırakılırsa sahnede aranır.")]
    [SerializeField] private HarmonyMissionController missionController;

    [Tooltip("Eklem uzayından üretilen giderim yolu. Boş bırakılırsa sahnede " +
             "aranır; bulunamazsa yol kusur konumlarından kurulur.")]
    [SerializeField] private HarmonyCleaningJointPath cleaningJointPath;

    [Header("Topic")]
    [Tooltip("Tarama sırasında yayınlanan planlanan yol (nav_msgs/Path).")]
    [SerializeField] private string pathPlanTopic = "/harmony/path_plan";

    [Header("Görünüm")]
    [Tooltip("Çizgi kalınlığı [m].")]
    [SerializeField] private float lineWidth = 0.006f;

    [Tooltip("Tarama yolunun rengi.")]
    [SerializeField] private Color sensingColor = new Color32(0x06, 0xb6, 0xd4, 200);

    [Tooltip("Giderim yolunun rengi.")]
    [SerializeField] private Color cleaningColor = new Color32(0xa8, 0x55, 0xf7, 200);

    [Tooltip("Yolun kusurdan önce yaklaşacağı noktayı da çiz. " +
             "Kusur gövdesinde 'approach' alanı olarak gelir.")]
    [SerializeField] private bool includeApproachPoints = true;

    [Tooltip("Giderim yolunun sonunda başlangıç noktasına dönüş çizilsin mi? " +
             "Robot yalnızca en sonda HOME'a döner.")]
    [SerializeField] private bool closeCleaningPath;

    [Header("Hata Ayıklama")]
    [SerializeField] private bool verboseLogging;

    private LineRenderer line;

    // ROS geri çağrısı arka thread'den gelir.
    private readonly ConcurrentQueue<RosPath> pathQueue = new ConcurrentQueue<RosPath>();

    // En son alınan tarama yolu (ROS world koordinatlarında).
    private readonly List<Vector3> sensingPathRos = new List<Vector3>();

    // Çizim için hazırlanan AR noktaları.
    private readonly List<Vector3> arPoints = new List<Vector3>();

    private void Awake()
    {
        line = GetComponent<LineRenderer>();
        line.useWorldSpace = true;
        line.widthMultiplier = lineWidth;
        line.numCornerVertices = 4;
        line.numCapVertices = 4;
        line.positionCount = 0;

        // Işıktan bağımsız, her koşulda okunur bir malzeme.
        if (line.material == null || line.material.shader == null)
            line.material = new Material(Shader.Find("Sprites/Default"));
    }

    private void Start()
    {
        if (mapper == null) mapper = FindObjectOfType<HarmonyDefectSurfaceMapper>();
        if (defectSubscriber == null) defectSubscriber = FindObjectOfType<HarmonyDefectSubscriber>();
        if (missionController == null) missionController = FindObjectOfType<HarmonyMissionController>();
        if (cleaningJointPath == null) cleaningJointPath = FindObjectOfType<HarmonyCleaningJointPath>();

        if (cleaningJointPath == null)
            Debug.Log("HarmonyGhostTrajectory: HarmonyCleaningJointPath yok; giderim yolu " +
                      "kusur konumlarından kurulacak (kolun ara hareketi görünmez).");

        if (mapper == null)
            Debug.LogWarning("HarmonyGhostTrajectory: HarmonyDefectSurfaceMapper bulunamadı; " +
                             "yörünge çizilemeyecek.");

        if (rosConnector?.RosSocket != null)
        {
            rosConnector.RosSocket.Subscribe<RosPath>(pathPlanTopic, ReceivePath);
            Debug.Log($"HarmonyGhostTrajectory: abone olundu → {pathPlanTopic}");
        }
        else
        {
            Debug.LogWarning("HarmonyGhostTrajectory: RosSocket yok; tarama yolu alınamayacak. " +
                             "Giderim yolu kusur listesinden yine de çizilir.");
        }
    }

    private void ReceivePath(RosPath msg)
    {
        if (msg?.poses == null || msg.poses.Length == 0) return;
        pathQueue.Enqueue(msg);
    }

    private void Update()
    {
        while (pathQueue.TryDequeue(out RosPath msg))
            StoreSensingPath(msg);
    }

    /// <summary>
    /// Gelen yol mesajını ROS world noktalarına çevirip saklar.
    /// </summary>
    /// <param name="msg">nav_msgs/Path gövdesi.</param>
    private void StoreSensingPath(RosPath msg)
    {
        sensingPathRos.Clear();

        for (int i = 0; i < msg.poses.Length; i++)
        {
            var p = msg.poses[i].pose.position;
            sensingPathRos.Add(new Vector3((float)p.x, (float)p.y, (float)p.z));
        }

        if (verboseLogging)
            Debug.Log($"HarmonyGhostTrajectory: tarama yolu alındı, {sensingPathRos.Count} nokta.");
    }

    private void LateUpdate()
    {
        if (mapper == null) { line.positionCount = 0; return; }

        MissionState state = missionController != null ? missionController.State : MissionState.Unknown;

        // Giderim aşamasında robotun eklem yolundan üretilen TCP yolu, diğer
        // hallerde ROS'un yayınladığı tarama yolu gösterilir.
        IReadOnlyList<Vector3> rosPoints = (state == MissionState.Cleaning)
            ? CleaningPath()
            : sensingPathRos;

        Color color = (state == MissionState.Cleaning) ? cleaningColor : sensingColor;

        if (rosPoints == null || rosPoints.Count < 2)
        {
            line.positionCount = 0;
            return;
        }

        arPoints.Clear();
        for (int i = 0; i < rosPoints.Count; i++)
        {
            if (mapper.TryRosToAr(rosPoints[i], out Vector3 ar))
                arPoints.Add(ar);
        }

        if (arPoints.Count < 2)
        {
            // Yüzey henüz hazır değil (anchor'lar yerleşmemiş olabilir).
            line.positionCount = 0;
            return;
        }

        line.startColor = color;
        line.endColor = color;
        line.widthMultiplier = lineWidth;
        line.positionCount = arPoints.Count;
        line.SetPositions(arPoints.ToArray());
    }

    // Giderim yolu her karede yeniden kurulmasın diye tamponlanır.
    private readonly List<Vector3> cleaningPathBuffer = new List<Vector3>();

    /// <summary>
    /// Giderim yolunu verir. Eklem uzayından üretilen yol varsa o kullanılır;
    /// yoksa kusur konumlarından kurulan eski yola düşülür.
    /// </summary>
    /// <returns>ROS world koordinatlarında yol noktaları.</returns>
    private IReadOnlyList<Vector3> CleaningPath()
    {
        if (cleaningJointPath != null)
        {
            IReadOnlyList<Vector3> fromJoints = cleaningJointPath.RosPoints;
            if (fromJoints != null && fromJoints.Count >= 2) return fromJoints;
        }

        return BuildCleaningPath();
    }

    /// <summary>
    /// Kusur konumlarından giderim yolunu kurar. Sıra
    /// <see cref="CleaningOrder"/> ile sabittir; listede bulunmayan kusurlar
    /// sona, geliş sırasıyla eklenir.
    /// </summary>
    /// <returns>ROS world koordinatlarında yol noktaları.</returns>
    private List<Vector3> BuildCleaningPath()
    {
        cleaningPathBuffer.Clear();

        if (defectSubscriber == null || defectSubscriber.DefectCount == 0)
            return cleaningPathBuffer;

        var visited = new HashSet<string>();

        for (int i = 0; i < CleaningOrder.Length; i++)
        {
            HarmonyDefect d = defectSubscriber.GetDefect(CleaningOrder[i]);
            if (d == null) continue;
            AppendDefectStop(d);
            visited.Add(d.Id);
        }

        // Senaryo dışı kusur gelirse yolun dışında kalmasın.
        var all = defectSubscriber.Defects;
        for (int i = 0; i < all.Count; i++)
        {
            if (visited.Contains(all[i].Id)) continue;
            AppendDefectStop(all[i]);
        }

        if (closeCleaningPath && cleaningPathBuffer.Count > 1)
            cleaningPathBuffer.Add(cleaningPathBuffer[0]);

        return cleaningPathBuffer;
    }

    /// <summary>
    /// Bir kusurun yaklaşma ve temas noktalarını yola ekler.
    /// </summary>
    /// <param name="defect">Kusur kaydı.</param>
    private void AppendDefectStop(HarmonyDefect defect)
    {
        if (includeApproachPoints && defect.RosApproach != Vector3.zero)
            cleaningPathBuffer.Add(defect.RosApproach);

        cleaningPathBuffer.Add(defect.RosPosition);

        // Robot temas sonrası yaklaşma noktasına geri çekilir; çizgi de öyle
        // görünsün ki sıradaki kusura geçiş yüzeyin içinden geçiyormuş gibi
        // görünmesin.
        if (includeApproachPoints && defect.RosApproach != Vector3.zero)
            cleaningPathBuffer.Add(defect.RosApproach);
    }
}
