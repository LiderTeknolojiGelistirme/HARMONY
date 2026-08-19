using RosSharp.RosBridgeClient;
using RosSharp.RosBridgeClient.MessageTypes.Sensor;
using UnityEngine;
using TMPro;
using Zenject;
using System.Collections.Generic;

public class RobotSpecsSubscriber : MonoBehaviour
{
    [Inject] private RosConnector rosConnector;
    [Inject] private GameConfig gameConfig;

    [Header("Topic Ayarları")]
    [Tooltip("ROS joint_states topic.\n\n" +
             "hil_test (fake hw):  /joint_states   (ur10e_* joints)\n" +
             "hil_test (sim):      /sim/joint_states (sim_ur10e_* joints)\n" +
             "ifarlab_gazebo:      /joint_states   (sim_ur10e_* + joint1-6 + wheel)")]
    [SerializeField] private string topicName = "/joint_states";

    [Tooltip("Robot numarası (1 veya 2) - UI ataması için")]
    [SerializeField] private int robotNumber = 1;

    [Header("UI Text Elemanları")]
    [SerializeField] private TMP_Text velocityText;
    [SerializeField] private TMP_Text forceText;
    [SerializeField] private TMP_Text positionsText;

    private string[] filteredNames;
    private double[] filteredPositions;
    private double[] filteredVelocities;
    private double[] filteredEfforts;
    private bool isMessageReceived;
    private int msgCount;

    /// <summary> Filtrelenmiş joint isimleri. </summary>
    public string[] FilteredNames => filteredNames;
    /// <summary> Joint pozisyonları (rad). </summary>
    public double[] FilteredPositions => filteredPositions;
    /// <summary> Joint hızları (rad/s). </summary>
    public double[] FilteredVelocities => filteredVelocities;
    /// <summary> Joint kuvvetleri (Nm). </summary>
    public double[] FilteredEfforts => filteredEfforts;

    void Start()
    {
        if (rosConnector == null)
        {
            Debug.LogError($"RobotSpecsSubscriber (Robot {robotNumber}): RosConnector inject edilemedi!");
            return;
        }

        if (gameConfig != null)
        {
            if (robotNumber == 1)
            {
                if (velocityText == null) velocityText = gameConfig.robot1VelocityText;
                if (forceText == null) forceText = gameConfig.robot1ForceText;
                if (positionsText == null) positionsText = gameConfig.robot1PositionsText;
            }
            else if (robotNumber == 2)
            {
                if (velocityText == null) velocityText = gameConfig.robot2VelocityText;
                if (forceText == null) forceText = gameConfig.robot2ForceText;
                if (positionsText == null) positionsText = gameConfig.robot2PositionsText;
            }
        }

        if (rosConnector.RosSocket == null)
        {
            Debug.LogError($"RobotSpecsSubscriber (Robot {robotNumber}): RosSocket null!");
            return;
        }

        rosConnector.RosSocket.Subscribe<JointState>(topicName, ReceiveJointStateMessage);
        isMessageReceived = false;
        msgCount = 0;

        Debug.Log($"RobotSpecsSubscriber (Robot {robotNumber}): {topicName} topic'ine abone olundu.");
    }

    private void ReceiveJointStateMessage(JointState message)
    {
        if (message.name == null || message.name.Length == 0)
            return;

        msgCount++;
        if (msgCount <= 3)
        {
            Debug.Log($"RobotSpecsSubscriber: Mesaj #{msgCount} alındı. " +
                      $"Joint sayısı={message.name.Length}, " +
                      $"İlk joint={message.name[0]}");
        }

        var names = new List<string>();
        var positions = new List<double>();
        var velocities = new List<double>();
        var efforts = new List<double>();

        for (int i = 0; i < message.name.Length; i++)
        {
            string jn = message.name[i];

            if (!IsRelevantJoint(jn))
                continue;

            names.Add(jn);
            positions.Add(i < message.position.Length ? message.position[i] : 0.0);
            velocities.Add(i < message.velocity.Length ? message.velocity[i] : 0.0);
            efforts.Add(i < message.effort.Length ? message.effort[i] : 0.0);
        }

        if (names.Count == 0)
            return;

        filteredNames = names.ToArray();
        filteredPositions = positions.ToArray();
        filteredVelocities = velocities.ToArray();
        filteredEfforts = efforts.ToArray();
        isMessageReceived = true;
    }

    private bool IsRelevantJoint(string jointName)
    {
        // AGV tekerleklerini her zaman filtrele
        if (jointName.StartsWith("wheel_"))
            return false;

        // UR10e joint'leri: ur10e_* veya sim_ur10e_* veya robotX_ur10e_*
        if (jointName.Contains("ur10e"))
            return true;

        // Kawasaki joint'leri (joint1-joint6) — ifarlab_gazebo senaryosu için
        if (jointName.StartsWith("joint"))
            return true;

        return false;
    }

    void Update()
    {
        if (isMessageReceived)
        {
            if (msgCount <= 3)
            {
                Debug.Log($"RobotSpecsSubscriber Update: filteredNames={filteredNames?.Length}, " +
                          $"velText={velocityText != null}, forceText={forceText != null}, posText={positionsText != null}");
            }
            UpdateUI();
            isMessageReceived = false;
        }
    }

    private void UpdateUI()
    {
        string[] colors = new string[]
        {
            "#C62828", "#1565C0", "#2E7D32",
            "#6A1B9A", "#E65100", "#00838F", "#4E342E"
        };

        if (velocityText != null && filteredNames != null &&
            filteredVelocities != null && filteredVelocities.Length > 0)
        {
            string info = "<b><color=#212121>Velocity</color></b>\n";
            for (int i = 0; i < filteredNames.Length && i < filteredVelocities.Length; i++)
            {
                string c = colors[i % colors.Length];
                info += $"<b><color={c}>{GetShortJointName(filteredNames[i])}: {filteredVelocities[i]:F3}</color></b>\n";
            }
            velocityText.text = info;
        }

        if (forceText != null && filteredNames != null &&
            filteredEfforts != null && filteredEfforts.Length > 0)
        {
            string info = "<b><color=#212121>Force</color></b>\n";
            for (int i = 0; i < filteredNames.Length && i < filteredEfforts.Length; i++)
            {
                string c = colors[i % colors.Length];
                info += $"<b><color={c}>{GetShortJointName(filteredNames[i])}: {filteredEfforts[i]:F3}</color></b>\n";
            }
            forceText.text = info;
        }

        if (positionsText != null && filteredNames != null &&
            filteredPositions != null && filteredPositions.Length > 0)
        {
            string info = "<b><color=#212121>Positions</color></b>\n";
            for (int i = 0; i < filteredNames.Length && i < filteredPositions.Length; i++)
            {
                string c = colors[i % colors.Length];
                info += $"<b><color={c}>{GetShortJointName(filteredNames[i])}: {filteredPositions[i]:F3}</color></b>\n";
            }
            positionsText.text = info;
        }
    }

    private string GetShortJointName(string fullName)
    {
        string s = fullName;

        // "sim_ur10e_shoulder_pan_joint" → strip after "_ur10e_"
        // "ur10e_shoulder_pan_joint"     → strip "ur10e_" prefix
        // "robot1_ur10e_elbow_joint"     → strip after "_ur10e_"
        int idx = s.IndexOf("_ur10e_");
        if (idx >= 0)
        {
            s = s.Substring(idx + 7);
        }
        else if (s.StartsWith("ur10e_"))
        {
            s = s.Substring(6);
        }

        if (s.EndsWith("_joint"))
            s = s.Substring(0, s.Length - 6);

        // "joint1" → "J1"
        if (s.StartsWith("joint") && s.Length <= 6)
            s = "J" + s.Substring(5);

        if (s == "base_to_robot_mount")
            s = "linear_axis";

        if (s == "gripper")
            s = "gripper";

        return s;
    }

    void OnDestroy() { }

    public class Factory : PlaceholderFactory<RobotSpecsSubscriber> { }
}
