using UnityEngine;
using RosSharp.RosBridgeClient;
using TMPro;
using UnityEngine.UI;

[CreateAssetMenu(fileName = "GameConfig", menuName = "HARMONY/GameConfig")]
public class GameConfig : ScriptableObject
{
    [Header("ROS Ayarları")]
    public RosConnector rosConnector;
    
    [Header("Exec Command Ayarları")]
    public string execTopic = "/exec/run";
    
    [Header("Image Subscriber Ayarları")]
    public string imageTopicName = "/image_robot1";
    public Renderer imagePlaneRenderer;
    
    [Header("UI Image Ayarları")]
    public UnityEngine.UI.Image robot1Image;
    public UnityEngine.UI.Image robot2Image;
    
    [Header("Joint State Subscriber Ayarları")]
    public string jointStateTopicName = "/robot1/joint_states";
    
    [Header("Magician Simulator Ayarları")]
    public TMP_Text errorText;
    public Button crButton;
    
    [Header("Robot Specs UI Ayarları - Robot 1")]
    public TMP_Text robot1VelocityText;
    public TMP_Text robot1ForceText;
    public TMP_Text robot1PositionsText;
    
    [Header("Robot Specs UI Ayarları - Robot 2")]
    public TMP_Text robot2VelocityText;
    public TMP_Text robot2ForceText;
    public TMP_Text robot2PositionsText;
    
    [Header("Task List UI Ayarları - Robot 1")]
    public TMP_Text robot1CurrentTaskText;
    public TMP_Text robot1PreviousTasksText;
    public TMP_Text robot1MagicianStateText;
    
    [Header("Task List UI Ayarları - Robot 2")]
    public TMP_Text robot2CurrentTaskText;
    public TMP_Text robot2PreviousTasksText;
    public TMP_Text robot2MagicianStateText;
    
    [Header("LOG Panel Ayarları")]
    public TMP_Text logText;
    
    [Header("XBOT UI - Joint Detail Ayarları - Robot 1")]
    public TMP_Text robot1PositionReferenceText;
    public TMP_Text robot1MotorPositionText;
    public TMP_Text robot1LinkPositionText;
    public TMP_Text robot1VelocityReferenceText;
    public TMP_Text robot1MotorVelocityText;
    public TMP_Text robot1LinkVelocityText;
    public TMP_Text robot1TorqueRefFfwdText;
    public TMP_Text robot1TorqueRefImpText;
    public TMP_Text robot1TorqueText;
    public TMP_Text robot1StiffnessText;
    public TMP_Text robot1DampingText;
    public TMP_Text robot1MotorTemperatureText;
    public TMP_Text robot1DriverTemperatureText;
    public TMP_Text robot1FaultText;
    
    [Header("XBOT UI - Joint Detail Ayarları - Robot 2")]
    public TMP_Text robot2PositionReferenceText;
    public TMP_Text robot2MotorPositionText;
    public TMP_Text robot2LinkPositionText;
    public TMP_Text robot2VelocityReferenceText;
    public TMP_Text robot2MotorVelocityText;
    public TMP_Text robot2LinkVelocityText;
    public TMP_Text robot2TorqueRefFfwdText;
    public TMP_Text robot2TorqueRefImpText;
    public TMP_Text robot2TorqueText;
    public TMP_Text robot2StiffnessText;
    public TMP_Text robot2DampingText;
    public TMP_Text robot2MotorTemperatureText;
    public TMP_Text robot2DriverTemperatureText;
    public TMP_Text robot2FaultText;
    
    [Header("XBOT UI - Robot Status Ayarları - Robot 1")]
    public TMP_Text robot1StatusText;
    public TMP_Text robot1VoltageText;
    
    [Header("XBOT UI - Robot Status Ayarları - Robot 2")]
    public TMP_Text robot2StatusText;
    public TMP_Text robot2VoltageText;
    
    [Header("XBOT UI - Joint Slider Ayarları - Robot 1")]
    public Slider[] robot1JointSliders = new Slider[6];
    public TMP_Text[] robot1JointValueTexts = new TMP_Text[6];
    
    [Header("XBOT UI - Joint Slider Ayarları - Robot 2")]
    public Slider[] robot2JointSliders = new Slider[6];
    public TMP_Text[] robot2JointValueTexts = new TMP_Text[6];
    
    [Header("XBOT UI - Controller State Ayarları - Robot 1")]
    public TMP_Text robot1RosControlStatusText;
    public TMP_Text robot1RosIoStatusText;
    
    [Header("XBOT UI - Controller State Ayarları - Robot 2")]
    public TMP_Text robot2RosControlStatusText;
    public TMP_Text robot2RosIoStatusText;
    
    [Header("Robot Model Referansları")]
    [Tooltip("Unity'deki UR10 robot modeli (UR10 GameObject) - Robot 1")]
    public GameObject robot1Model;
    
    [Tooltip("Unity'deki UR10 robot modeli (UR10 GameObject) - Robot 2")]
    public GameObject robot2Model;
}



