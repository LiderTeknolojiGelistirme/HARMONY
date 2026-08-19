using RosSharp.RosBridgeClient;
using RosSharp.RosBridgeClient.MessageTypes.Sensor;
using UnityEngine;
using TMPro;
using Zenject;
using System.Collections.Generic;

public class JointDetailSubscriber : MonoBehaviour
{
    [Inject] private RosConnector rosConnector;
    [Inject] private GameConfig gameConfig;
    
    [Header("Topic ve UI Ayarları")]
    [Tooltip("ROS topic name for joint state subscription.")]
    [SerializeField] private string topicName = "/robot1/joint_states";
    
    [Tooltip("Robot numarası (1 veya 2)")]
    [SerializeField] private int robotNumber = 1;
    
    [Tooltip("Hangi joint'in detaylarını gösterecek (örn: 'joint2')")]
    [SerializeField] private string selectedJointName = "joint2";
    
    [Header("UI Text Elemanları - Position")]
    [SerializeField] private TMP_Text positionReferenceText;
    [SerializeField] private TMP_Text motorPositionText;
    [SerializeField] private TMP_Text linkPositionText;
    
    [Header("UI Text Elemanları - Velocity")]
    [SerializeField] private TMP_Text velocityReferenceText;
    [SerializeField] private TMP_Text motorVelocityText;
    [SerializeField] private TMP_Text linkVelocityText;
    
    [Header("UI Text Elemanları - Torque")]
    [SerializeField] private TMP_Text torqueRefFfwdText;
    [SerializeField] private TMP_Text torqueRefImpText;
    [SerializeField] private TMP_Text torqueText;
    
    [Header("UI Text Elemanları - Control")]
    [SerializeField] private TMP_Text stiffnessText;
    [SerializeField] private TMP_Text dampingText;
    
    [Header("UI Text Elemanları - Temperature & Fault")]
    [SerializeField] private TMP_Text motorTemperatureText;
    [SerializeField] private TMP_Text driverTemperatureText;
    [SerializeField] private TMP_Text faultText;
    
    // Gelen eklem verilerini saklamak için değişkenler
    private Dictionary<string, JointData> jointDataMap = new Dictionary<string, JointData>();
    private bool isMessageReceived;
    
    private class JointData
    {
        public double position;
        public double velocity;
        public double effort;
    }

    void Start()
    {
        if (rosConnector == null)
        {
            Debug.LogError($"JointDetailSubscriber (Robot {robotNumber}): RosConnector inject edilemedi!");
            return;
        }
        
        // Topic adını belirle
        if (string.IsNullOrEmpty(topicName))
        {
            topicName = $"/robot{robotNumber}/joint_states";
        }
        
        // UI Text elemanlarını GameConfig'den al
        if (gameConfig != null)
        {
            if (robotNumber == 1)
            {
                if (positionReferenceText == null) positionReferenceText = gameConfig.robot1PositionReferenceText;
                if (motorPositionText == null) motorPositionText = gameConfig.robot1MotorPositionText;
                if (linkPositionText == null) linkPositionText = gameConfig.robot1LinkPositionText;
                if (velocityReferenceText == null) velocityReferenceText = gameConfig.robot1VelocityReferenceText;
                if (motorVelocityText == null) motorVelocityText = gameConfig.robot1MotorVelocityText;
                if (linkVelocityText == null) linkVelocityText = gameConfig.robot1LinkVelocityText;
                if (torqueRefFfwdText == null) torqueRefFfwdText = gameConfig.robot1TorqueRefFfwdText;
                if (torqueRefImpText == null) torqueRefImpText = gameConfig.robot1TorqueRefImpText;
                if (torqueText == null) torqueText = gameConfig.robot1TorqueText;
                if (stiffnessText == null) stiffnessText = gameConfig.robot1StiffnessText;
                if (dampingText == null) dampingText = gameConfig.robot1DampingText;
                if (motorTemperatureText == null) motorTemperatureText = gameConfig.robot1MotorTemperatureText;
                if (driverTemperatureText == null) driverTemperatureText = gameConfig.robot1DriverTemperatureText;
                if (faultText == null) faultText = gameConfig.robot1FaultText;
            }
            else if (robotNumber == 2)
            {
                if (positionReferenceText == null) positionReferenceText = gameConfig.robot2PositionReferenceText;
                if (motorPositionText == null) motorPositionText = gameConfig.robot2MotorPositionText;
                if (linkPositionText == null) linkPositionText = gameConfig.robot2LinkPositionText;
                if (velocityReferenceText == null) velocityReferenceText = gameConfig.robot2VelocityReferenceText;
                if (motorVelocityText == null) motorVelocityText = gameConfig.robot2MotorVelocityText;
                if (linkVelocityText == null) linkVelocityText = gameConfig.robot2LinkVelocityText;
                if (torqueRefFfwdText == null) torqueRefFfwdText = gameConfig.robot2TorqueRefFfwdText;
                if (torqueRefImpText == null) torqueRefImpText = gameConfig.robot2TorqueRefImpText;
                if (torqueText == null) torqueText = gameConfig.robot2TorqueText;
                if (stiffnessText == null) stiffnessText = gameConfig.robot2StiffnessText;
                if (dampingText == null) dampingText = gameConfig.robot2DampingText;
                if (motorTemperatureText == null) motorTemperatureText = gameConfig.robot2MotorTemperatureText;
                if (driverTemperatureText == null) driverTemperatureText = gameConfig.robot2DriverTemperatureText;
                if (faultText == null) faultText = gameConfig.robot2FaultText;
            }
        }
        
        if (rosConnector.RosSocket == null)
        {
            Debug.LogError($"JointDetailSubscriber (Robot {robotNumber}): RosSocket null!");
            return;
        }
        
        rosConnector.RosSocket.Subscribe<JointState>(topicName, ReceiveJointStateMessage);
        isMessageReceived = false;
        
        Debug.Log($"JointDetailSubscriber (Robot {robotNumber}): {topicName} topic'ine abone olundu. Seçili joint: {selectedJointName}");
    }

    private void ReceiveJointStateMessage(JointState message)
    {
        // Tüm joint verilerini dictionary'ye kaydet
        if (message.name != null && message.position != null)
        {
            for (int i = 0; i < message.name.Length && i < message.position.Length; i++)
            {
                string jointName = message.name[i];
                jointDataMap[jointName] = new JointData
                {
                    position = i < message.position.Length ? message.position[i] : 0,
                    velocity = i < message.velocity.Length ? message.velocity[i] : 0,
                    effort = i < message.effort.Length ? message.effort[i] : 0
                };
            }
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
        if (!jointDataMap.ContainsKey(selectedJointName))
        {
            return; // Seçili joint bulunamadı
        }
        
        JointData jointData = jointDataMap[selectedJointName];
        
        // Position bilgileri
        // Not: JointState'te sadece link position var, motor position ve reference için dynamic_joint_states gerekir
        // Şimdilik link position'ı her ikisine de yazıyoruz
        if (linkPositionText != null)
        {
            linkPositionText.text = jointData.position.ToString("F2");
        }
        if (motorPositionText != null)
        {
            motorPositionText.text = jointData.position.ToString("F2"); // Geçici olarak aynı değer
        }
        if (positionReferenceText != null)
        {
            positionReferenceText.text = jointData.position.ToString("F2"); // Geçici olarak aynı değer
        }
        
        // Velocity bilgileri
        if (linkVelocityText != null)
        {
            linkVelocityText.text = jointData.velocity.ToString("F2");
        }
        if (motorVelocityText != null)
        {
            motorVelocityText.text = jointData.velocity.ToString("F2"); // Geçici olarak aynı değer
        }
        if (velocityReferenceText != null)
        {
            velocityReferenceText.text = jointData.velocity.ToString("F2"); // Geçici olarak aynı değer
        }
        
        // Torque bilgileri (effort = torque)
        if (torqueText != null)
        {
            torqueText.text = jointData.effort.ToString("F2");
        }
        if (torqueRefFfwdText != null)
        {
            torqueRefFfwdText.text = jointData.effort.ToString("F2"); // Geçici olarak aynı değer
        }
        if (torqueRefImpText != null)
        {
            torqueRefImpText.text = jointData.effort.ToString("F2"); // Geçici olarak aynı değer
        }
        
        // Stiffness ve Damping (JointState'te yok, varsayılan değerler)
        if (stiffnessText != null)
        {
            stiffnessText.text = "0.00"; // JointState'te yok
        }
        if (dampingText != null)
        {
            dampingText.text = "0.00"; // JointState'te yok
        }
        
        // Temperature ve Fault (JointState'te yok, varsayılan değerler)
        if (motorTemperatureText != null)
        {
            motorTemperatureText.text = "0.00"; // JointState'te yok
        }
        if (driverTemperatureText != null)
        {
            driverTemperatureText.text = "0.00"; // JointState'te yok
        }
        if (faultText != null)
        {
            faultText.text = "0.00"; // JointState'te yok
        }
    }
    
    // Seçili joint'i değiştirmek için public metod
    public void SetSelectedJoint(string jointName)
    {
        selectedJointName = jointName;
    }
    
    void OnDestroy()
    {
        jointDataMap.Clear();
    }
    
    // Factory için
    public class Factory : PlaceholderFactory<JointDetailSubscriber> { }
}


