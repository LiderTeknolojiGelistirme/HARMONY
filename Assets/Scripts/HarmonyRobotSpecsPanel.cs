using System.Text;
using TMPro;
using UnityEngine;

/// <summary>
/// Robot Specs panelini sürer.
///
/// Web arayüzündeki üç canlı grafiğin (Joint Positions, Joint Velocities,
/// Cleaning Force) AR karşılığıdır. Gözlükte ince çizgili grafik okunaksız
/// kaldığı için grafik yerine sayısal gösterim kullanılır.
///
/// Panel yenileme hızı ekrandaki titremeyi önlemek için sınırlandırılır;
/// telemetri 100 Hz'e kadar gelebilir, metni o hızda yazmanın anlamı yok.
/// </summary>
public class HarmonyRobotSpecsPanel : MonoBehaviour
{
    [Header("Bağımlılıklar")]
    [Tooltip("Telemetri kaynağı. Boş bırakılırsa sahnede aranır.")]
    [SerializeField] private HarmonyTelemetrySubscriber telemetry;

    [Header("UI Alanları")]
    [Tooltip("Eklem konumları sütunu.")]
    [SerializeField] private TMP_Text positionText;

    [Tooltip("Eklem hızları sütunu.")]
    [SerializeField] private TMP_Text velocityText;

    [Tooltip("Temizleme kuvveti sütunu.")]
    [SerializeField] private TMP_Text forceText;

    [Header("Görünüm")]
    [Tooltip("Saniyede kaç kez yenilensin.")]
    [Range(1f, 20f)]
    [SerializeField] private float refreshRateHz = 5f;

    [Tooltip("Eklem açılarını derece olarak göster (kapalıysa radyan).")]
    [SerializeField] private bool showDegrees = true;

    private readonly StringBuilder builder = new StringBuilder(256);
    private float nextRefreshTime;

    private void Start()
    {
        if (telemetry == null)
            telemetry = FindObjectOfType<HarmonyTelemetrySubscriber>();

        if (telemetry == null)
            Debug.LogWarning("HarmonyRobotSpecsPanel: HarmonyTelemetrySubscriber bulunamadı, " +
                             "panel boş kalacak.");
    }

    private void Update()
    {
        if (telemetry == null) return;
        if (Time.time < nextRefreshTime) return;

        nextRefreshTime = Time.time + (1f / Mathf.Max(refreshRateHz, 0.1f));

        RedrawPositions();
        RedrawVelocities();
        RedrawForce();
    }

    /// <summary>
    /// Eklem konumlarını listeler. Lineer eksen (base_to_robot_mount) metre
    /// cinsindendir, döner eklemlerden farklı birimde gösterilir.
    /// </summary>
    private void RedrawPositions()
    {
        if (positionText == null) return;

        if (!telemetry.HasJointData)
        {
            positionText.text = "<b>Positions:</b>\n<i>waiting for data</i>";
            return;
        }

        builder.Clear();
        builder.Append("<b>Positions:</b>\n");

        var joints = telemetry.TrackedJoints;
        for (int i = 0; i < joints.Count; i++)
        {
            string joint = joints[i];
            if (!telemetry.TryGetPosition(joint, out double value)) continue;

            builder.Append("<size=80%>").Append(ShortName(joint)).Append("</size>  ");

            if (IsLinearJoint(joint))
                builder.Append(value.ToString("F3")).Append(" m");
            else if (showDegrees)
                builder.Append((value * Mathf.Rad2Deg).ToString("F1")).Append("°");
            else
                builder.Append(value.ToString("F3")).Append(" rad");

            builder.Append('\n');
        }

        positionText.text = builder.ToString().TrimEnd('\n');
    }

    /// <summary>
    /// Eklem hızlarını listeler ve en yüksek mutlak hızı özet olarak verir.
    /// </summary>
    private void RedrawVelocities()
    {
        if (velocityText == null) return;

        if (!telemetry.HasJointData)
        {
            velocityText.text = "<b>Velocity:</b>\n<i>waiting for data</i>";
            return;
        }

        builder.Clear();
        builder.Append("<b>Velocity:</b>\n");

        var joints = telemetry.TrackedJoints;
        for (int i = 0; i < joints.Count; i++)
        {
            string joint = joints[i];
            if (!telemetry.TryGetVelocity(joint, out double value)) continue;

            builder.Append("<size=80%>").Append(ShortName(joint)).Append("</size>  ");

            if (IsLinearJoint(joint))
                builder.Append(value.ToString("F3")).Append(" m/s");
            else if (showDegrees)
                builder.Append((value * Mathf.Rad2Deg).ToString("F1")).Append("°/s");
            else
                builder.Append(value.ToString("F3")).Append(" rad/s");

            builder.Append('\n');
        }

        builder.Append("<size=80%>max  ")
               .Append(telemetry.MaxAbsVelocity.ToString("F3"))
               .Append("</size>");

        velocityText.text = builder.ToString();
    }

    /// <summary>
    /// Temizleme kuvvetini gösterir. Senaryoda anlamlı olan bileşen Z'dir
    /// (yüzeye bastırma kuvveti).
    /// </summary>
    private void RedrawForce()
    {
        if (forceText == null) return;

        if (!telemetry.HasForceData)
        {
            forceText.text = "<b>Force:</b>\n<i>waiting for data</i>";
            return;
        }

        forceText.text =
            "<b>Force:</b>\n" +
            $"Fz  {telemetry.ForceZ:F2} N\n" +
            $"<size=80%>|F|  {telemetry.ForceMagnitude:F2} N</size>";
    }

    /// <summary>
    /// Uzun eklem adını panelde sığacak kısa etikete çevirir.
    /// </summary>
    /// <param name="jointSuffix">Öneksiz eklem adı.</param>
    private static string ShortName(string jointSuffix)
    {
        switch (jointSuffix)
        {
            case "shoulder_pan_joint": return "pan";
            case "shoulder_lift_joint": return "lift";
            case "elbow_joint": return "elbow";
            case "wrist_1_joint": return "w1";
            case "wrist_2_joint": return "w2";
            case "wrist_3_joint": return "w3";
            case "base_to_robot_mount": return "rail";
            default: return jointSuffix;
        }
    }

    /// <summary>
    /// Eklem prizmatik mi (metre cinsinden)? Lineer eksen döner eklemlerden
    /// farklı birim kullanır.
    /// </summary>
    /// <param name="jointSuffix">Öneksiz eklem adı.</param>
    private static bool IsLinearJoint(string jointSuffix)
        => jointSuffix == "base_to_robot_mount";
}
