using RosSharp.RosBridgeClient;
using RosSharp.RosBridgeClient.MessageTypes.Std;
using UnityEngine;
using TMPro;
using Zenject;
using System.Collections.Generic;

public class RosLogSubscriber : MonoBehaviour
{
    [Inject] private RosConnector rosConnector;
    [Inject] private GameConfig gameConfig;
    
    [Header("Topic ve UI Ayarları")]
    [Tooltip("ROS topic name for log subscription.")]
    [SerializeField] private string topicName = "/rosout";
    
    [Header("UI Text Elemanı")]
    [Tooltip("Log mesajlarını gösterecek Text elemanı (ScrollView içinde olabilir)")]
    [SerializeField] private TMP_Text logText;
    
    [Header("Log Ayarları")]
    [Tooltip("Maksimum gösterilecek log satırı sayısı")]
    [SerializeField] private int maxLogLines = 100;
    
    private Queue<string> logQueue = new Queue<string>();
    private bool isMessageReceived;

    void Start()
    {
        if (rosConnector == null)
        {
            Debug.LogError("RosLogSubscriber: RosConnector inject edilemedi! SceneContext ve GameInstaller kontrol edin.");
            return;
        }
        
        // Topic adını belirle
        if (string.IsNullOrEmpty(topicName))
        {
            topicName = "/rosout";
        }
        
        // UI Text elemanını GameConfig'den al (eğer atanmamışsa)
        if (logText == null && gameConfig != null)
        {
            logText = gameConfig.logText;
        }
        
        if (logText == null)
        {
            Debug.LogWarning("RosLogSubscriber: Log Text atanmamış! Inspector'dan veya GameConfig'den atayın.");
        }
        
        if (rosConnector.RosSocket == null)
        {
            Debug.LogError("RosLogSubscriber: RosSocket null! RosConnector bağlantısını kontrol edin.");
            return;
        }
        
        // RosConnector'dan RosSocket'i al ve topic'e abone ol
        // Not: RosSharp'da Log mesaj tipi olmayabilir, bu durumda String kullanabiliriz
        try
        {
            // /rosout topic'i genellikle String mesajı yayınlar
            rosConnector.RosSocket.Subscribe<String>(topicName, ReceiveLogMessage);
            isMessageReceived = false;
            Debug.Log($"RosLogSubscriber: {topicName} topic'ine abone olundu.");
        }
        catch (System.Exception e)
        {
            Debug.LogError($"RosLogSubscriber: Topic'e abone olunurken hata: {e.Message}");
        }
    }

    // Bu fonksiyon, ROS'tan her yeni Log mesajı geldiğinde OTOMATİK olarak çalıştırılır
    private void ReceiveLogMessage(String message)
    {
        // String mesajını direkt kullan
        string logMessage = message.data;
        
        // Boş mesajları atla
        if (string.IsNullOrEmpty(logMessage))
        {
            return;
        }
        
        // Timestamp ekle
        string timestampedMessage = $"[{System.DateTime.Now:HH:mm:ss}] {logMessage}";
        
        // Thread-safe olmayan ama Unity main thread'de çalıştığı için sorun yok
        lock (logQueue)
        {
            logQueue.Enqueue(timestampedMessage);
            
            // Maksimum satır sayısını aşarsa eski logları sil
            while (logQueue.Count > maxLogLines)
            {
                logQueue.Dequeue();
            }
        }
        
        isMessageReceived = true;
    }
        
    // Update fonksiyonu her frame'de çalışır, Unity'nin ana döngüsüdür.
    void Update()
    {
        // Eğer yeni bir mesaj geldiyse
        if(isMessageReceived && logText != null)
        {
            UpdateUI();
            isMessageReceived = false; 
        }
    }
    
    private void UpdateUI()
    {
        if (logText == null) return;
        
        lock (logQueue)
        {
            // Tüm log mesajlarını birleştir
            string[] logArray = logQueue.ToArray();
            logText.text = string.Join("\n", logArray);
        }
    }
    
    void OnDestroy()
    {
        lock (logQueue)
        {
            logQueue.Clear();
        }
    }
    
    // Factory için
    public class Factory : PlaceholderFactory<RosLogSubscriber> { }
}

