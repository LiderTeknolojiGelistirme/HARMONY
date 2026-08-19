using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// HARMONY hücresindeki UR10e + lineer eksen düzeneğinin ileri kinematiği.
///
/// <see cref="Ur10eForwardKinematics"/> yalnız kolu, UR denetleyicisinin kendi
/// "Base" çerçevesinde çözer. Bu bileşen sonucu ROS <c>world</c> çerçevesine
/// taşır: robot bir lineer eksen üzerinde durduğu için taban konumu ray
/// konumunun fonksiyonudur.
///
/// Eklem sırası ROS tarafıyla aynıdır (harmony_defects.py, cleaning.json):
///   [0] ur10e_base_to_robot_mount  (ray, prizmatik, metre)
///   [1] ur10e_shoulder_pan_joint
///   [2] ur10e_shoulder_lift_joint
///   [3] ur10e_elbow_joint
///   [4] ur10e_wrist_1_joint
///   [5] ur10e_wrist_2_joint
///   [6] ur10e_wrist_3_joint
///
/// Varsayılan kalibrasyon whole_cell_hw.urdf.xacro zincirinden çıkarıldı:
///   world → ur10e_table            : (0, 0, 0)
///   → ur10e_robot_mount (ray)      : (-0.158, 0.115, 0.58635), eksen +Y
///   → ur10e_base_link              : (0, 0, 0)
///   → ur10e_base_link_inertia      : (0.052439, 0.0768, 0.05815)
/// DH taban çerçevesi base_link_inertia ile çakışır; toplamı
/// <see cref="railOrigin"/> varsayılanını verir. Dönüş, UR'nin kendi Base
/// çerçevesine geçiş olan z ekseni etrafında 180 derecedir (URDF'teki
/// <c>base_link-base_fixed_joint</c>).
///
/// DİKKAT — model farkı: Hücrenin URDF'i CAD'den üretilmiş ve bağ ölçüleri
/// UR10e'nin DH parametrelerinden bir miktar sapıyor. Kayıtlı giderim yolu
/// boyunca iki modelin verdiği TCP konumu ortalama 9.5 mm, en kötü 11.6 mm
/// ayrışıyor (ölçüm: 03.08.2026, 572 eklem noktası). Burada DH modeli
/// kullanılıyor; çünkü gerçek robot kaydına karşı doğrulanmış olan odur
/// (bkz. <see cref="Ur10eForwardKinematics"/>). Fark, hayalet çizginin AR
/// yerleşim hatasının (elle konan dört anchor, santimetre mertebesi) altında
/// kalır ancak URDF tarafında düzeltilmesi gereken ayrı bir konudur.
/// </summary>
public class HarmonyRobotKinematics : MonoBehaviour
{
    /// <summary>Ray dahil toplam eklem sayısı.</summary>
    public const int ConfigurationLength = 7;

    [Header("Hücre Yerleşimi (ROS world, metre)")]
    [Tooltip("Ray sıfırdayken UR10e taban (DH) çerçevesinin ROS world konumu.")]
    [SerializeField] private Vector3 railOrigin = new Vector3(-0.105561f, 0.1918f, 0.6445f);

    [Tooltip("Lineer eksenin ROS world'deki birim yön vektörü.")]
    [SerializeField] private Vector3 railAxis = new Vector3(0f, 1f, 0f);

    [Tooltip("Taban çerçevesinin ROS world z ekseni etrafındaki dönüşü [derece]. " +
             "UR'nin Base çerçevesi base_link'e göre 180 derece dönüktür.")]
    [SerializeField] private float baseYawDegrees = 180f;

    [Header("Takım")]
    [Tooltip("Flanştan takım ucuna z yönündeki uzaklık [m]. " +
             "model.py'deki T_6_Tool[2,3] ile aynı; takım takılı değilse 0. " +
             "IFARLAB'da uç takım değişirse burası güncellenmelidir.")]
    [SerializeField] private float toolOffset;

    [Header("Ray Sınırları")]
    [Tooltip("URDF'teki prizmatik eklem sınırları [m]. Dışına düşen değerler " +
             "ayrıntılı günlük açıkken uyarı olarak bildirilir.")]
    [SerializeField] private Vector2 railLimits = new Vector2(0.05f, 1.95f);

    [Header("Hata Ayıklama")]
    [SerializeField] private bool verboseLogging;

    // Kare başına ayırma olmasın diye tamponlar örnek üzerinde tutulur.
    private readonly double[] tcpTransform = new double[16];
    private readonly double[] jointBuffer = new double[Ur10eForwardKinematics.JointCount];

    private bool railWarningIssued;

    /// <summary>Flanştan takım ucuna öteleme [m].</summary>
    public float ToolOffset
    {
        get { return toolOffset; }
        set { toolOffset = value; }
    }

    /// <summary>
    /// Yedi elemanlı eklem yapılandırmasından TCP'nin ROS world konumunu verir.
    /// </summary>
    /// <param name="configuration">
    /// [ray(m), pan, lift, elbow, w1, w2, w3] — açılar radyan.
    /// </param>
    /// <returns>ROS world çerçevesinde TCP konumu [m].</returns>
    public Vector3 TcpInRosWorld(IList<double> configuration)
    {
        if (configuration == null || configuration.Count < ConfigurationLength)
        {
            Debug.LogWarning("HarmonyRobotKinematics: eksik eklem yapılandırması " +
                             "(7 değer bekleniyor).");
            return Vector3.zero;
        }

        for (int i = 0; i < Ur10eForwardKinematics.JointCount; i++)
            jointBuffer[i] = configuration[i + 1];

        return TcpInRosWorld((float)configuration[0], jointBuffer);
    }

    /// <summary>
    /// Ray konumu ve altı eklem açısından TCP'nin ROS world konumunu verir.
    /// </summary>
    /// <param name="rail">Lineer eksen konumu [m].</param>
    /// <param name="jointAngles">q1…q6 [rad].</param>
    /// <returns>ROS world çerçevesinde TCP konumu [m].</returns>
    public Vector3 TcpInRosWorld(float rail, IList<double> jointAngles)
    {
        Ur10eForwardKinematics.Solve(jointAngles, toolOffset, tcpTransform);

        double bx, by, bz;
        Ur10eForwardKinematics.GetPosition(tcpTransform, out bx, out by, out bz);

        if (verboseLogging && !railWarningIssued && (rail < railLimits.x || rail > railLimits.y))
        {
            Debug.LogWarning(string.Format(
                "HarmonyRobotKinematics: ray konumu {0:F3} m sınırların ({1:F2}–{2:F2}) dışında. " +
                "Bu uyarı bir kez gösterilir.", rail, railLimits.x, railLimits.y));
            railWarningIssued = true;
        }

        return BaseToRosWorld(rail, (float)bx, (float)by, (float)bz);
    }

    /// <summary>
    /// Taban (DH) çerçevesindeki bir noktayı ROS world çerçevesine taşır.
    /// </summary>
    /// <param name="rail">Lineer eksen konumu [m].</param>
    /// <param name="x">Taban çerçevesinde x [m].</param>
    /// <param name="y">Taban çerçevesinde y [m].</param>
    /// <param name="z">Taban çerçevesinde z [m].</param>
    /// <returns>ROS world konumu [m].</returns>
    private Vector3 BaseToRosWorld(float rail, float x, float y, float z)
    {
        float yaw = baseYawDegrees * Mathf.Deg2Rad;
        float c = Mathf.Cos(yaw);
        float s = Mathf.Sin(yaw);

        var rotated = new Vector3(c * x - s * y, s * x + c * y, z);
        return railOrigin + railAxis * rail + rotated;
    }
}
