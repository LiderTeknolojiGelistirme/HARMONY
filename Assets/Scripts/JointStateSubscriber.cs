using RosSharp.RosBridgeClient;
using RosSharp.RosBridgeClient.MessageTypes.Sensor;
using UnityEngine;
using Zenject;

public class JointStateSubscriber : MonoBehaviour
{
    [Inject] private RosConnector rosConnector;
    [Inject] private GameConfig gameConfig;
    
    private string topicName;

    // Gelen eklem verilerini saklamak için değişkenler
    private string[] jointNames;
    private double[] jointPositions;
    private bool isMessageReceived;

    void Start()
    {
        if (rosConnector == null)
        {
            Debug.LogError("JointStateSubscriber: RosConnector inject edilemedi! SceneContext ve GameInstaller kontrol edin.");
            return;
        }
        
        topicName = gameConfig != null ? gameConfig.jointStateTopicName : "/robot1/joint_states";
        
        if (rosConnector.RosSocket == null)
        {
            Debug.LogError("JointStateSubscriber: RosSocket null! RosConnector bağlantısını kontrol edin.");
            return;
        }
        
        // RosConnector'dan RosSocket'i al ve topic'e abone ol
        rosConnector.RosSocket.Subscribe<JointState>(topicName, ReceiveJointStateMessage);
        isMessageReceived = false;
    }

    // Bu fonksiyon, ROS'tan her yeni JointState mesajı geldiğinde OTOMATİK olarak çalıştırılır
    private void ReceiveJointStateMessage(JointState message)
    {
        // Gelen mesajdaki dizileri kendi değişkenlerimize kopyalayalım
        jointNames = message.name;
        jointPositions = message.position;
            
        // Mesajın geldiğini belirtelim
        isMessageReceived = true;
    }
        
    // Update fonksiyonu her frame'de çalışır, Unity'nin ana döngüsüdür.
    // Log basma işlemini burada yaparak konsolun aşırı dolmasını engelleyebiliriz.
    void Update()
    {
        // Eğer yeni bir mesaj geldiyse
        if(isMessageReceived)
        {
            Debug.Log("--- YENİ EKLEM POZİSYONLARI ALINDI ---");

            // Bütün eklemlerin isimlerini ve pozisyonlarını döngü ile yazdıralım
            for (int i = 0; i < jointNames.Length; i++)
            {
                // Her bir eklemin adını ve pozisyonunu ayrı ayrı loglayalım
                Debug.Log("Joint: " + jointNames[i] + ", Pozisyon: " + jointPositions[i]);
            }
                
            // Mesajı işlediğimiz için tekrar false yapalım ki her frame'de aynı mesajı basmasın
            isMessageReceived = false; 
        }
    }
    
    // Factory için
    public class Factory : PlaceholderFactory<JointStateSubscriber> { }
}
