using RosSharp.RosBridgeClient;
using RosSharp.RosBridgeClient.MessageTypes.Std;
using UnityEngine;
using TMPro;
using Zenject;

public class RobotStatusSubscriber : MonoBehaviour
{
    [Inject] private RosConnector rosConnector;
    [Inject] private GameConfig gameConfig;

    [Header("Topic Ayarları")]
    [Tooltip("ROS topic: /harmony/robot_status (std_msgs/String)")]
    [SerializeField] private string topicName = "/harmony/robot_status";

    [Tooltip("Robot numarası (1 veya 2) - UI ataması için")]
    [SerializeField] private int robotNumber = 1;

    [Header("UI Text Elemanları")]
    [Tooltip("Gelen mesajın ham içeriğini gösterecek TMP Text")]
    [SerializeField] private TMP_Text statusText;

    private string latestMessage;
    private bool isMessageReceived;
    private int msgCount;

    /// <summary> Son alınan durum mesajı. </summary>
    public string LatestMessage => latestMessage;

    void Start()
    {
        if (rosConnector == null)
        {
            Debug.LogError($"RobotStatusSubscriber (Robot {robotNumber}): RosConnector inject edilemedi!");
            return;
        }

        if (gameConfig != null)
        {
            if (robotNumber == 1)
            {
                if (statusText == null) statusText = gameConfig.robot1StatusText;
            }
            else if (robotNumber == 2)
            {
                if (statusText == null) statusText = gameConfig.robot2StatusText;
            }
        }

        if (rosConnector.RosSocket == null)
        {
            Debug.LogError($"RobotStatusSubscriber (Robot {robotNumber}): RosSocket null!");
            return;
        }

        rosConnector.RosSocket.Subscribe<String>(topicName, ReceiveStatusMessage);
        isMessageReceived = false;
        msgCount = 0;

        Debug.Log($"RobotStatusSubscriber (Robot {robotNumber}): {topicName} topic'ine abone olundu.");
    }

    private void ReceiveStatusMessage(String message)
    {
        if (message == null || message.data == null)
            return;

        msgCount++;
        if (msgCount <= 3)
        {
            Debug.Log($"RobotStatusSubscriber: Mesaj #{msgCount} alındı. data=\"{message.data}\"");
        }

        latestMessage = message.data;
        isMessageReceived = true;
    }

    void Update()
    {
        if (isMessageReceived)
        {
            if (statusText != null)
            {
                statusText.text = $"<b><color=#1A237E>{latestMessage}</color></b>";
            }
            isMessageReceived = false;
        }
    }

    void OnDestroy() { }

    public class Factory : PlaceholderFactory<RobotStatusSubscriber> { }
}
