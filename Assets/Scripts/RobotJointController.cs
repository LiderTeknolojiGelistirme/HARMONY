using RosSharp.RosBridgeClient;
using RosSharp.RosBridgeClient.MessageTypes.Sensor;
using UnityEngine;
using Zenject;
using System.Collections.Generic;

public class RobotJointController : MonoBehaviour
{
    [Inject] private RosConnector rosConnector;
    [Inject] private GameConfig gameConfig;
    
    [Header("Topic Ayarları")]
    [Tooltip("ROS joint_states topic.\n\n" +
             "hil_test (fake hw):  /joint_states   (ur10e_* joints)\n" +
             "hil_test (sim):      /sim/joint_states (sim_ur10e_* joints)\n" +
             "ifarlab_gazebo:      /joint_states   (sim_ur10e_* + joint1-6 + wheel)")]
    [SerializeField] private string topicName = "/joint_states";
    
    [Tooltip("Robot numarası (1 veya 2) - GameConfig model ataması için")]
    [SerializeField] private int robotNumber = 1;
    
    [Header("Robot Model Referansları")]
    [Tooltip("Unity'deki UR10 robot modelinin root GameObject'i")]
    [SerializeField] private GameObject robotModel;
    
    [Header("Hareket Ayarları")]
    [SerializeField] private float lerpSpeed = 10f;
    [SerializeField] private bool useRadianValues = true;
    [SerializeField] private bool autoFixHierarchy = true;
    
    // Suffix → Unity joint eşlemesi (prefix-bağımsız)
    private static readonly string[] JointSuffixes = new string[]
    {
        "shoulder_pan_joint",
        "shoulder_lift_joint",
        "elbow_joint",
        "wrist_1_joint",
        "wrist_2_joint",
        "wrist_3_joint"
    };
    
    private static readonly string[] UnityJointNames = new string[]
    {
        "UR10_Joint1",
        "UR10_Joint2",
        "UR10_Joint3",
        "UR10_Joint4",
        "UR10_Joint5",
        "UR10_Joint6"
    };
    
    private static readonly Vector3[] DefaultRotations = new Vector3[]
    {
        new Vector3(90, 0, 180),
        new Vector3(0, 270, 0),
        new Vector3(-180, 0, 0),
        new Vector3(-90, 0, 270),
        new Vector3(-90, 0, -180),
        new Vector3(0, 0, 0)
    };
    
    private static readonly Vector3[] RotationAxes = new Vector3[]
    {
        Vector3.forward,
        Vector3.up,
        Vector3.up,
        Vector3.forward,
        Vector3.forward,
        Vector3.forward
    };

    private class JointInfo
    {
        public Transform transform;
        public Vector3 defaultRotation;
        public Vector3 rotationAxis;
    }

    // suffix → JointInfo (runtime'da ROS mesajından gelen isimle suffix eşlemesi yapılır)
    private Dictionary<string, JointInfo> jointBySuffix = new Dictionary<string, JointInfo>();
    private Dictionary<string, double> targetPositions = new Dictionary<string, double>();
    private bool isMessageReceived;
    private int msgCount;

    void Start()
    {
        if (rosConnector == null)
        {
            Debug.LogError($"RobotJointController (Robot {robotNumber}): RosConnector inject edilemedi!");
            return;
        }
        
        if (robotModel == null && gameConfig != null)
        {
            if (robotNumber == 1)
                robotModel = gameConfig.robot1Model;
            else if (robotNumber == 2)
                robotModel = gameConfig.robot2Model;
        }
        
        if (robotModel == null)
        {
            Debug.LogError($"RobotJointController (Robot {robotNumber}): Robot model atanmamış!");
            return;
        }
        
        InitializeJoints();
        
        if (autoFixHierarchy)
            FixHierarchy();
        
        if (rosConnector.RosSocket == null)
        {
            Debug.LogError($"RobotJointController (Robot {robotNumber}): RosSocket null!");
            return;
        }
        
        rosConnector.RosSocket.Subscribe<JointState>(topicName, ReceiveJointStateMessage);
        isMessageReceived = false;
        msgCount = 0;
        
        Debug.Log($"RobotJointController (Robot {robotNumber}): {topicName} topic'ine abone olundu. " +
                  $"{jointBySuffix.Count} joint eşleştirildi.");
    }

    private void InitializeJoints()
    {
        for (int i = 0; i < JointSuffixes.Length && i < UnityJointNames.Length; i++)
        {
            Transform jt = FindChildRecursive(robotModel.transform, UnityJointNames[i]);
            if (jt != null)
            {
                var info = new JointInfo
                {
                    transform = jt,
                    defaultRotation = DefaultRotations[i],
                    rotationAxis = RotationAxes[i]
                };
                jointBySuffix[JointSuffixes[i]] = info;
                jt.localRotation = Quaternion.Euler(DefaultRotations[i]);
            }
            else
            {
                Debug.LogWarning($"RobotJointController: {UnityJointNames[i]} bulunamadı!");
            }
        }
    }

    private string MatchSuffix(string rosJointName)
    {
        // "sim_ur10e_shoulder_pan_joint" → "shoulder_pan_joint"
        // "ur10e_elbow_joint"            → "elbow_joint"
        // "robot1_ur10e_wrist_1_joint"   → "wrist_1_joint"
        for (int i = 0; i < JointSuffixes.Length; i++)
        {
            if (rosJointName.EndsWith(JointSuffixes[i]))
                return JointSuffixes[i];
        }
        return null;
    }

    private void ReceiveJointStateMessage(JointState message)
    {
        if (message.name == null || message.position == null)
            return;

        msgCount++;
        if (msgCount <= 3)
        {
            Debug.Log($"RobotJointController: Mesaj #{msgCount}, " +
                      $"joint sayısı={message.name.Length}, ilk={message.name[0]}");
        }

        for (int i = 0; i < message.name.Length && i < message.position.Length; i++)
        {
            string suffix = MatchSuffix(message.name[i]);
            if (suffix != null && jointBySuffix.ContainsKey(suffix))
            {
                targetPositions[suffix] = message.position[i];
            }
        }
        
        isMessageReceived = true;
    }
        
    void Update()
    {
        if (isMessageReceived)
        {
            UpdateRobotJoints();
            isMessageReceived = false;
        }
    }
    
    private void UpdateRobotJoints()
    {
        foreach (var kvp in jointBySuffix)
        {
            if (!targetPositions.ContainsKey(kvp.Key))
                continue;

            JointInfo info = kvp.Value;
            float targetAngle = (float)targetPositions[kvp.Key];
            if (useRadianValues)
                targetAngle *= Mathf.Rad2Deg;
            
            Quaternion defaultRot = Quaternion.Euler(info.defaultRotation);
            Quaternion additionalRot = Quaternion.AngleAxis(targetAngle, info.rotationAxis);
            Quaternion targetRot = defaultRot * additionalRot;
            
            info.transform.localRotation = Quaternion.Lerp(
                info.transform.localRotation,
                targetRot,
                Time.deltaTime * lerpSpeed
            );
        }
    }

    private void FixHierarchy()
    {
        var ordered = new List<Transform>();
        for (int i = 0; i < JointSuffixes.Length; i++)
        {
            if (jointBySuffix.ContainsKey(JointSuffixes[i]))
                ordered.Add(jointBySuffix[JointSuffixes[i]].transform);
        }

        if (ordered.Count < 2)
            return;
        
        for (int i = 0; i < ordered.Count - 1; i++)
        {
            Transform current = ordered[i];
            Transform next = ordered[i + 1];
            
            if (next.parent != current)
            {
                Vector3 savedPos = next.position;
                Quaternion savedRot = next.rotation;
                
                next.SetParent(current);
                
                next.localPosition = current.InverseTransformPoint(savedPos);
                next.localRotation = Quaternion.Inverse(current.rotation) * savedRot;
            }
        }
    }
    
    private Transform FindChildRecursive(Transform parent, string name)
    {
        foreach (Transform child in parent)
        {
            if (child.name == name)
                return child;
            
            Transform found = FindChildRecursive(child, name);
            if (found != null)
                return found;
        }
        return null;
    }
    
    void OnDestroy()
    {
        targetPositions.Clear();
        jointBySuffix.Clear();
    }
    
    public class Factory : PlaceholderFactory<RobotJointController> { }
}
