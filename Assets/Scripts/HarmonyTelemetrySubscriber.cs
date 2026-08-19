using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using RosSharp.RosBridgeClient;
using UnityEngine;
using Zenject;

using RosJointState = RosSharp.RosBridgeClient.MessageTypes.Sensor.JointState;
using RosWrenchStamped = RosSharp.RosBridgeClient.MessageTypes.Geometry.WrenchStamped;

/// <summary>
/// Robot telemetrisini ROS'tan toplar: eklem konumları/hızları ve temizleme
/// kuvveti. Web arayüzündeki üç canlı grafiğin (Joint Positions, Joint
/// Velocities, Cleaning Force) veri kaynağıyla aynıdır; AR tarafında grafik
/// yerine sayısal gösterim tercih edildiği için burada yalnızca son değerler
/// tutulur, geçmiş tampon tutulmaz.
///
/// Eklem adları önek-bağımsız eşleştirilir: gerçek tarafta "ur10e_elbow_joint",
/// simülasyonda "sim_ur10e_elbow_joint" gelir; ikisi de "elbow_joint" olarak
/// kaydedilir.
/// </summary>
public class HarmonyTelemetrySubscriber : MonoBehaviour
{
    [Inject] private RosConnector rosConnector;

    [Header("Topic Ayarları")]
    [Tooltip("Eklem durumları. Gerçek taraf /joint_states, simülasyon /sim/joint_states.")]
    [SerializeField] private string jointStateTopic = "/joint_states";

    [Tooltip("Temizleme kuvveti (geometry_msgs/WrenchStamped).")]
    [SerializeField] private string forceTopic = "/harmony/cleaning/force";

    [Header("Gösterilecek Eklemler")]
    [Tooltip("Önek atıldıktan sonra eşleşecek eklem adları. Sıra, panelde görünecek sıradır.")]
    [SerializeField]
    private string[] trackedJointSuffixes =
    {
        "shoulder_pan_joint",
        "shoulder_lift_joint",
        "elbow_joint",
        "wrist_1_joint",
        "wrist_2_joint",
        "wrist_3_joint",
        "base_to_robot_mount"
    };

    // Eklem adı (öneksiz) -> son değer.
    private readonly Dictionary<string, double> jointPositions = new Dictionary<string, double>();
    private readonly Dictionary<string, double> jointVelocities = new Dictionary<string, double>();

    // ROS# geri çağrıları arka thread'den gelir.
    private readonly ConcurrentQueue<RosJointState> jointQueue = new ConcurrentQueue<RosJointState>();
    private readonly ConcurrentQueue<RosWrenchStamped> forceQueue = new ConcurrentQueue<RosWrenchStamped>();

    /// <summary>Telemetri güncellendiğinde tetiklenir.</summary>
    public event Action OnTelemetryUpdated;

    /// <summary>Temizleme kuvvetinin Z bileşeni [N]. Senaryoda bastırma kuvveti budur.</summary>
    public double ForceZ { get; private set; }

    /// <summary>Kuvvet vektörünün büyüklüğü [N].</summary>
    public double ForceMagnitude { get; private set; }

    /// <summary>En az bir eklem mesajı alındı mı?</summary>
    public bool HasJointData { get; private set; }

    /// <summary>En az bir kuvvet mesajı alındı mı?</summary>
    public bool HasForceData { get; private set; }

    /// <summary>Panelde gösterilecek eklem adları (öneksiz), tanımlı sırada.</summary>
    public IReadOnlyList<string> TrackedJoints => trackedJointSuffixes;

    private void Start()
    {
        if (rosConnector?.RosSocket == null)
        {
            Debug.LogError("HarmonyTelemetrySubscriber: RosSocket null! Telemetri dinlenemeyecek.");
            return;
        }

        rosConnector.RosSocket.Subscribe<RosJointState>(jointStateTopic, ReceiveJointState);
        rosConnector.RosSocket.Subscribe<RosWrenchStamped>(forceTopic, ReceiveForce);

        Debug.Log($"HarmonyTelemetrySubscriber: abone olundu → {jointStateTopic}, {forceTopic}");
    }

    private void ReceiveJointState(RosJointState message)
    {
        if (message?.name == null) return;
        jointQueue.Enqueue(message);
    }

    private void ReceiveForce(RosWrenchStamped message)
    {
        if (message?.wrench == null) return;
        forceQueue.Enqueue(message);
    }

    private void Update()
    {
        bool changed = false;

        while (jointQueue.TryDequeue(out RosJointState msg))
        {
            ApplyJointState(msg);
            changed = true;
        }

        while (forceQueue.TryDequeue(out RosWrenchStamped msg))
        {
            ApplyForce(msg);
            changed = true;
        }

        if (changed)
            OnTelemetryUpdated?.Invoke();
    }

    /// <summary>
    /// Gelen eklem mesajındaki izlenen eklemleri sözlüklere yazar.
    /// Mesajda bulunmayan eklemler önceki değerlerini korur — /joint_states'e
    /// birden fazla broadcaster yayın yaptığı için her mesaj tüm eklemleri
    /// içermez.
    /// </summary>
    /// <param name="msg">Gelen JointState mesajı.</param>
    private void ApplyJointState(RosJointState msg)
    {
        int count = msg.name.Length;

        for (int i = 0; i < count; i++)
        {
            string suffix = MatchSuffix(msg.name[i]);
            if (suffix == null) continue;

            if (msg.position != null && i < msg.position.Length)
                jointPositions[suffix] = msg.position[i];

            if (msg.velocity != null && i < msg.velocity.Length)
                jointVelocities[suffix] = msg.velocity[i];

            HasJointData = true;
        }
    }

    private void ApplyForce(RosWrenchStamped msg)
    {
        double fx = msg.wrench.force.x;
        double fy = msg.wrench.force.y;
        double fz = msg.wrench.force.z;

        ForceZ = fz;
        ForceMagnitude = Math.Sqrt(fx * fx + fy * fy + fz * fz);
        HasForceData = true;
    }

    /// <summary>
    /// ROS eklem adını izlenen son eke eşler. "sim_ur10e_elbow_joint" ve
    /// "ur10e_elbow_joint" ikisi de "elbow_joint" döner.
    /// </summary>
    /// <param name="rosJointName">Ham ROS eklem adı.</param>
    /// <returns>Eşleşen ek; eşleşme yoksa null.</returns>
    private string MatchSuffix(string rosJointName)
    {
        if (string.IsNullOrEmpty(rosJointName)) return null;

        for (int i = 0; i < trackedJointSuffixes.Length; i++)
        {
            if (rosJointName.EndsWith(trackedJointSuffixes[i], StringComparison.Ordinal))
                return trackedJointSuffixes[i];
        }
        return null;
    }

    /// <summary>
    /// Eklemin son konumunu verir.
    /// </summary>
    /// <param name="jointSuffix">Öneksiz eklem adı.</param>
    /// <param name="value">Konum [rad veya m].</param>
    /// <returns>Değer varsa true.</returns>
    public bool TryGetPosition(string jointSuffix, out double value)
        => jointPositions.TryGetValue(jointSuffix, out value);

    /// <summary>
    /// Eklemin son hızını verir.
    /// </summary>
    /// <param name="jointSuffix">Öneksiz eklem adı.</param>
    /// <param name="value">Hız [rad/s veya m/s].</param>
    /// <returns>Değer varsa true.</returns>
    public bool TryGetVelocity(string jointSuffix, out double value)
        => jointVelocities.TryGetValue(jointSuffix, out value);

    /// <summary>
    /// Test/simülasyon amaçlı: tek bir eklemin konum ve hızını doğrudan yazar.
    /// ROS akışı olmadan grafikleri ve telemetri panelini beslemek için.
    /// </summary>
    /// <param name="jointSuffix">Öneksiz eklem adı, örn. "elbow_joint".</param>
    /// <param name="position">Konum [rad veya m].</param>
    /// <param name="velocity">Hız [rad/s veya m/s].</param>
    public void InjectJoint(string jointSuffix, double position, double velocity)
    {
        if (string.IsNullOrEmpty(jointSuffix)) return;
        jointPositions[jointSuffix] = position;
        jointVelocities[jointSuffix] = velocity;
        HasJointData = true;
        OnTelemetryUpdated?.Invoke();
    }

    /// <summary>
    /// Test/simülasyon amaçlı: temizleme kuvvetini doğrudan yazar.
    /// </summary>
    /// <param name="forceZ">Z bileşeni [N].</param>
    public void InjectForce(double forceZ)
    {
        ForceZ = forceZ;
        ForceMagnitude = System.Math.Abs(forceZ);
        HasForceData = true;
        OnTelemetryUpdated?.Invoke();
    }

    /// <summary>
    /// İzlenen tüm eklemlerin hızlarının mutlak değerce en büyüğü.
    /// Panelde tek satırlık özet göstermek için kullanılır.
    /// </summary>
    public double MaxAbsVelocity
    {
        get
        {
            double max = 0.0;
            foreach (var kv in jointVelocities)
            {
                double abs = Math.Abs(kv.Value);
                if (abs > max) max = abs;
            }
            return max;
        }
    }
}
