using RosSharp.RosBridgeClient;
using RosSharp.RosBridgeClient.MessageTypes.Std;
using UnityEngine;
using TMPro;
using Zenject;

public class ControllerStateSubscriber : MonoBehaviour
{
    [Inject] private RosConnector rosConnector;
    [Inject] private GameConfig gameConfig;
    
    [Header("Topic ve UI Ayarları")]
    [Tooltip("ros_control için topic")]
    [SerializeField] private string rosControlTopic = "/robot1/robot1_scaled_joint_trajectory_controller/state";
    
    [Tooltip("ros_io için topic (varsa)")]
    [SerializeField] private string rosIoTopic = "";
    
    [Tooltip("Robot numarası (1 veya 2)")]
    [SerializeField] private int robotNumber = 1;
    
    [Header("UI Text Elemanları - ros_control")]
    [SerializeField] private TMP_Text rosControlStatusText;
    
    [Header("UI Text Elemanları - ros_io")]
    [SerializeField] private TMP_Text rosIoStatusText;
    
    private string rosControlStatus = "Stopped 0.0 ms";
    private string rosIoStatus = "Stopped 0.0 ms";
    private bool isMessageReceived;

    void Start()
    {
        if (rosConnector == null)
        {
            Debug.LogError($"ControllerStateSubscriber (Robot {robotNumber}): RosConnector inject edilemedi!");
            return;
        }
        
        // Topic adlarını belirle
        if (string.IsNullOrEmpty(rosControlTopic))
        {
            rosControlTopic = $"/robot{robotNumber}/robot{robotNumber}_scaled_joint_trajectory_controller/state";
        }
        
        // UI Text elemanlarını GameConfig'den al
        if (gameConfig != null)
        {
            if (robotNumber == 1)
            {
                if (rosControlStatusText == null) rosControlStatusText = gameConfig.robot1RosControlStatusText;
                if (rosIoStatusText == null) rosIoStatusText = gameConfig.robot1RosIoStatusText;
            }
            else if (robotNumber == 2)
            {
                if (rosControlStatusText == null) rosControlStatusText = gameConfig.robot2RosControlStatusText;
                if (rosIoStatusText == null) rosIoStatusText = gameConfig.robot2RosIoStatusText;
            }
        }
        
        if (rosConnector.RosSocket == null)
        {
            Debug.LogError($"ControllerStateSubscriber (Robot {robotNumber}): RosSocket null!");
            return;
        }
        
        // ros_control topic'ine abone ol
        try
        {
            rosConnector.RosSocket.Subscribe<String>(rosControlTopic, ReceiveRosControlMessage);
            Debug.Log($"ControllerStateSubscriber (Robot {robotNumber}): {rosControlTopic} topic'ine abone olundu.");
        }
        catch (System.Exception e)
        {
            Debug.LogWarning($"ControllerStateSubscriber: ros_control topic'ine abone olunurken hata: {e.Message}");
        }
        
        // ros_io topic'ine abone ol (varsa)
        if (!string.IsNullOrEmpty(rosIoTopic))
        {
            try
            {
                rosConnector.RosSocket.Subscribe<String>(rosIoTopic, ReceiveRosIoMessage);
                Debug.Log($"ControllerStateSubscriber (Robot {robotNumber}): {rosIoTopic} topic'ine abone olundu.");
            }
            catch (System.Exception e)
            {
                Debug.LogWarning($"ControllerStateSubscriber: ros_io topic'ine abone olunurken hata: {e.Message}");
            }
        }
        
        isMessageReceived = false;
        UpdateUI();
    }

    private void ReceiveRosControlMessage(String message)
    {
        // Mesaj içeriğine göre durumu belirle
        string data = message.data.ToUpper();
        
        if (data.Contains("RUNNING") || data.Contains("ACTIVE") || data.Contains("EXECUTING"))
        {
            rosControlStatus = "Running 0.1 ms"; // Gerçek zamanı topic'ten alınabilir
        }
        else
        {
            rosControlStatus = "Stopped 0.0 ms";
        }
        
        isMessageReceived = true;
    }
    
    private void ReceiveRosIoMessage(String message)
    {
        // Mesaj içeriğine göre durumu belirle
        string data = message.data.ToUpper();
        
        if (data.Contains("RUNNING") || data.Contains("ACTIVE") || data.Contains("EXECUTING"))
        {
            rosIoStatus = "Running 0.1 ms";
        }
        else
        {
            rosIoStatus = "Stopped 0.0 ms";
        }
        
        isMessageReceived = true;
    }
        
    void Update()
    {
        if(isMessageReceived)
        {
            UpdateUI();
            isMessageReceived = false; 
        }
    }
    
    private void UpdateUI()
    {
        // ros_control status'u güncelle
        if (rosControlStatusText != null)
        {
            rosControlStatusText.text = rosControlStatus;
        }
        
        // ros_io status'u güncelle
        if (rosIoStatusText != null)
        {
            rosIoStatusText.text = rosIoStatus;
        }
    }
    
    void OnDestroy()
    {
        // Temizlik
    }
    
    // Factory için
    public class Factory : PlaceholderFactory<ControllerStateSubscriber> { }
}


