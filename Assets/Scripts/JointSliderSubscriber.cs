using RosSharp.RosBridgeClient;
using RosSharp.RosBridgeClient.MessageTypes.Sensor;
using UnityEngine;
using UnityEngine.UI;
using Zenject;
using System.Collections.Generic;

public class JointSliderSubscriber : MonoBehaviour
{
    [Inject] private RosConnector rosConnector;
    [Inject] private GameConfig gameConfig;
    
    [Header("Topic ve UI Ayarları")]
    [Tooltip("ROS topic name for joint state subscription.")]
    [SerializeField] private string topicName = "/robot1/joint_states";
    
    [Tooltip("Robot numarası (1 veya 2)")]
    [SerializeField] private int robotNumber = 1;
    
    [Header("UI Slider Elemanları")]
    [Tooltip("joint1-joint6 için slider'lar (sırayla)")]
    [SerializeField] private Slider[] jointSliders = new Slider[6];
    
    [Header("UI Text Elemanları (Slider değerleri için)")]
    [Tooltip("joint1-joint6 için değer text'leri (sırayla)")]
    [SerializeField] private TMPro.TMP_Text[] jointValueTexts = new TMPro.TMP_Text[6];
    
    [Header("Slider Ayarları")]
    [Tooltip("Slider'ların minimum değeri")]
    [SerializeField] private float minValue = 0f;
    
    [Tooltip("Slider'ların maksimum değeri")]
    [SerializeField] private float maxValue = 100f;
    
    [Tooltip("Hangi field gösterilecek (Position, Velocity, Effort)")]
    [SerializeField] private FieldType fieldType = FieldType.Position;
    
    public enum FieldType
    {
        Position,
        Velocity,
        Effort
    }
    
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
            Debug.LogError($"JointSliderSubscriber (Robot {robotNumber}): RosConnector inject edilemedi!");
            return;
        }
        
        // Topic adını belirle
        if (string.IsNullOrEmpty(topicName))
        {
            topicName = $"/robot{robotNumber}/joint_states";
        }
        
        // UI Slider elemanlarını GameConfig'den al
        if (gameConfig != null)
        {
            if (robotNumber == 1)
            {
                if (jointSliders[0] == null) jointSliders = gameConfig.robot1JointSliders;
                if (jointValueTexts[0] == null) jointValueTexts = gameConfig.robot1JointValueTexts;
            }
            else if (robotNumber == 2)
            {
                if (jointSliders[0] == null) jointSliders = gameConfig.robot2JointSliders;
                if (jointValueTexts[0] == null) jointValueTexts = gameConfig.robot2JointValueTexts;
            }
        }
        
        // Slider'ları başlat
        foreach (var slider in jointSliders)
        {
            if (slider != null)
            {
                slider.minValue = minValue;
                slider.maxValue = maxValue;
            }
        }
        
        if (rosConnector.RosSocket == null)
        {
            Debug.LogError($"JointSliderSubscriber (Robot {robotNumber}): RosSocket null!");
            return;
        }
        
        rosConnector.RosSocket.Subscribe<JointState>(topicName, ReceiveJointStateMessage);
        isMessageReceived = false;
        
        Debug.Log($"JointSliderSubscriber (Robot {robotNumber}): {topicName} topic'ine abone olundu. Field Type: {fieldType}");
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
        // joint1-joint6 için slider'ları güncelle
        for (int i = 0; i < 6; i++)
        {
            string jointName = $"joint{i + 1}";
            
            if (!jointDataMap.ContainsKey(jointName))
            {
                continue; // Bu joint bulunamadı
            }
            
            JointData jointData = jointDataMap[jointName];
            
            // Field type'a göre değeri al
            double value = 0;
            switch (fieldType)
            {
                case FieldType.Position:
                    value = jointData.position;
                    break;
                case FieldType.Velocity:
                    value = jointData.velocity;
                    break;
                case FieldType.Effort:
                    value = jointData.effort;
                    break;
            }
            
            // Değeri 0-100 aralığına normalize et
            // Not: Gerçek değer aralığına göre normalize edilmeli
            float normalizedValue = Mathf.Clamp((float)value * 10f, minValue, maxValue);
            
            // Slider'ı güncelle
            if (i < jointSliders.Length && jointSliders[i] != null)
            {
                jointSliders[i].value = normalizedValue;
            }
            
            // Text değerini güncelle
            if (i < jointValueTexts.Length && jointValueTexts[i] != null)
            {
                jointValueTexts[i].text = normalizedValue.ToString("F0");
            }
        }
    }
    
    // Field type'ı değiştirmek için public metod
    public void SetFieldType(FieldType type)
    {
        fieldType = type;
    }
    
    void OnDestroy()
    {
        jointDataMap.Clear();
    }
    
    // Factory için
    public class Factory : PlaceholderFactory<JointSliderSubscriber> { }
}


