using TMPro;
using UnityEngine;

/// <summary>
/// Grafik panelini sürer: web arayüzündeki üç canlı Chart.js panelinin
/// (Joint Positions / Joint Velocities / Cleaning Force) AR karşılığı.
///
/// <see cref="HarmonyTelemetrySubscriber"/>'dan gelen son değerleri sabit
/// aralıklarla örnekleyip <see cref="HarmonyLineChart"/> bileşenlerine yazar.
/// ROS 100 Hz'e kadar yayın yapabildiği için doğrudan her mesajı grafiğe
/// eklemek yerine burada örnekleme hızı sınırlandırılır.
/// </summary>
public class HarmonyChartsPanel : MonoBehaviour
{
    /// <summary>Grafiklerde çizilen eklemler ve sırası (web'deki legend sırası).</summary>
    private static readonly string[] JointOrder =
    {
        "shoulder_pan_joint",
        "wrist_3_joint",
        "elbow_joint",
        "wrist_2_joint",
        "base_to_robot_mount",
        "wrist_1_joint",
        "shoulder_lift_joint"
    };

    /// <summary>Legend etiketleri — web arayüzüyle birebir aynı.</summary>
    private static readonly string[] JointLabels =
    {
        "Shoulder Pan", "Wrist 3", "Elbow", "Wrist 2", "Linear Axis", "Wrist 1", "Shoulder Lift"
    };

    /// <summary>Seri renkleri — web arayüzündeki Chart.js paletiyle aynı.</summary>
    private static readonly Color[] JointColors =
    {
        new Color32(0x14, 0xb8, 0xa6, 255), // Shoulder Pan  — teal
        new Color32(0x06, 0xb6, 0xd4, 255), // Wrist 3       — cyan
        new Color32(0x10, 0xb9, 0x81, 255), // Elbow         — green
        new Color32(0xf5, 0x9e, 0x0b, 255), // Wrist 2       — amber
        new Color32(0xef, 0x44, 0x44, 255), // Linear Axis   — red
        new Color32(0xa8, 0x55, 0xf7, 255), // Wrist 1       — purple
        new Color32(0xec, 0x48, 0x99, 255)  // Shoulder Lift — pink
    };

    private static readonly Color ForceColor = new Color32(0x14, 0xb8, 0xa6, 255);

    [Header("Bağımlılıklar")]
    [Tooltip("Telemetri kaynağı. Boş bırakılırsa sahnede aranır.")]
    [SerializeField] private HarmonyTelemetrySubscriber telemetry;

    [Header("Grafikler")]
    [SerializeField] private HarmonyLineChart positionChart;
    [SerializeField] private HarmonyLineChart velocityChart;
    [SerializeField] private HarmonyLineChart forceChart;

    [Header("Eksen Etiketleri")]
    [Tooltip("Konum grafiğinin Y ekseni değerleri (üstten alta).")]
    [SerializeField] private TMP_Text positionAxisText;

    [SerializeField] private TMP_Text velocityAxisText;
    [SerializeField] private TMP_Text forceAxisText;

    [Header("Örnekleme")]
    [Tooltip("Saniyede kaç örnek alınsın.")]
    [Range(1f, 60f)]
    [SerializeField] private float sampleRateHz = 20f;

    private float nextSampleTime;

    private void Start()
    {
        if (telemetry == null)
            telemetry = FindObjectOfType<HarmonyTelemetrySubscriber>();

        if (telemetry == null)
            Debug.LogWarning("HarmonyChartsPanel: HarmonyTelemetrySubscriber bulunamadı, " +
                             "grafikler boş kalacak.");

        // Başlangıç aralıkları yalnızca ilk veriye kadar geçerlidir; ardından
        // eksenler gelen değerlere göre kendiliğinden ölçeklenir. Sabit aralık
        // kullanıldığında sınır dışına çıkan ölçümler kırpılıyordu.
        if (positionChart != null)
        {
            positionChart.ConfigureSeries(JointLabels, JointColors);
            positionChart.SetYRange(-2f, 1f);
            positionChart.EnableAutoScale();
            positionChart.ValueUnit = "rad";
        }

        if (velocityChart != null)
        {
            velocityChart.ConfigureSeries(JointLabels, JointColors);
            velocityChart.SetYRange(-1f, 1f);
            velocityChart.EnableAutoScale();
            velocityChart.ValueUnit = "rad/s";
        }

        if (forceChart != null)
        {
            forceChart.ConfigureSeries(new[] { "Force Z" }, new[] { ForceColor });
            forceChart.SetYRange(0f, 1f);
            forceChart.EnableAutoScale();
            forceChart.ValueUnit = "N";
        }

        RefreshAxisLabels();
    }

    private void Update()
    {
        if (telemetry == null) return;
        if (Time.time < nextSampleTime) return;

        nextSampleTime = Time.time + (1f / Mathf.Max(sampleRateHz, 0.1f));

        if (telemetry.HasJointData)
        {
            for (int i = 0; i < JointOrder.Length; i++)
            {
                if (positionChart != null && telemetry.TryGetPosition(JointOrder[i], out double p))
                    positionChart.AddSample(i, (float)p);

                if (velocityChart != null && telemetry.TryGetVelocity(JointOrder[i], out double v))
                    velocityChart.AddSample(i, (float)v);
            }
        }

        if (forceChart != null && telemetry.HasForceData)
            forceChart.AddSample(0, (float)telemetry.ForceZ);

        // Eksenler otomatik ölçeklendiği için etiketler de her örneklemede
        // yeniden yazılır; aksi halde çizgiler kayar, sayılar sabit kalırdı.
        RefreshAxisLabels();
    }

    /// <summary>
    /// Üç grafiğin Y ekseni etiketlerini güncel sınırlarına göre yeniler.
    /// </summary>
    private void RefreshAxisLabels()
    {
        WriteAxisLabels(positionAxisText, positionChart);
        WriteAxisLabels(velocityAxisText, velocityChart);
        WriteAxisLabels(forceAxisText, forceChart);
    }

    /// <summary>
    /// Y ekseni değer etiketlerini üstten alta doğru yazar.
    /// </summary>
    /// <param name="target">Etiketlerin yazılacağı metin alanı.</param>
    /// <param name="chart">Sınırları okunacak grafik.</param>
    private static void WriteAxisLabels(TMP_Text target, HarmonyLineChart chart)
    {
        if (target == null || chart == null) return;

        float min = chart.CurrentYMin;
        float max = chart.CurrentYMax;

        // Grafikteki yatay ızgara sayısıyla aynı olmalı (HarmonyLineChart.gridRows = 5).
        const int steps = 5;
        string format = PickFormat(max - min);
        var sb = new System.Text.StringBuilder(64);

        for (int i = steps; i >= 0; i--)
        {
            float v = Mathf.Lerp(min, max, i / (float)steps);
            sb.Append(v.ToString(format));
            if (i > 0) sb.Append('\n');
        }

        target.text = sb.ToString();
    }

    /// <summary>
    /// Eksen aralığının genişliğine göre uygun ondalık basamak biçimini seçer.
    /// Kuvvet onlarca newton, eklem hızı ise binde mertebesinde olabildiği için
    /// sabit biçim okunaksız kalıyordu.
    /// </summary>
    /// <param name="range">Eksenin üst ve alt sınırı arasındaki fark.</param>
    private static string PickFormat(float range)
    {
        float r = Mathf.Abs(range);
        if (r >= 100f) return "F0";
        if (r >= 10f) return "F1";
        if (r >= 1f) return "F2";
        return "F3";
    }
}
