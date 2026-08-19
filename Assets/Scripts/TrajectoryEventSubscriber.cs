using RosSharp.RosBridgeClient;
using RosSharp.RosBridgeClient.MessageTypes.Std;
using UnityEngine;
using TMPro;
using Zenject;
using System.Collections.Generic;

public class TrajectoryEventSubscriber : MonoBehaviour
{
    [Inject] private RosConnector rosConnector;
    [Inject] private GameConfig gameConfig;
    
    [Header("Topic ve UI Ayarları")]
    [Tooltip("ROS topic name for trajectory execution event subscription.")]
    [SerializeField] private string topicName = "/robot1/trajectory_execution_event";
    
    [Tooltip("Robot numarası (1 veya 2) - UI'da hangi robot için olduğunu belirler")]
    [SerializeField] private int robotNumber = 1;
    
    [Header("UI Text Elemanları")]
    [Tooltip("Current Task bilgisini gösterecek Text elemanı")]
    [SerializeField] private TMP_Text currentTaskText;
    
    [Tooltip("Previous Tasks listesini gösterecek Text elemanı")]
    [SerializeField] private TMP_Text previousTasksText;
    
    [Tooltip("MAGICIAN state bilgisini gösterecek Text elemanı")]
    [SerializeField] private TMP_Text magicianStateText;
    
    [Header("Task Ayarları")]
    [Tooltip("Maksimum gösterilecek previous task sayısı")]
    [SerializeField] private int maxPreviousTasks = 10;
    
    private Queue<string> previousTasksQueue = new Queue<string>();
    private string currentTask = "IDLE";
    private string currentState = "IDLE";
    private bool isMessageReceived;

    void Start()
    {
        if (rosConnector == null)
        {
            Debug.LogError($"TrajectoryEventSubscriber (Robot {robotNumber}): RosConnector inject edilemedi! SceneContext ve GameInstaller kontrol edin.");
            return;
        }
        
        // Topic adını belirle
        if (string.IsNullOrEmpty(topicName))
        {
            topicName = $"/robot{robotNumber}/trajectory_execution_event";
        }
        
        // UI Text elemanlarını GameConfig'den al (eğer atanmamışsa)
        if (gameConfig != null)
        {
            if (robotNumber == 1)
            {
                if (currentTaskText == null) currentTaskText = gameConfig.robot1CurrentTaskText;
                if (previousTasksText == null) previousTasksText = gameConfig.robot1PreviousTasksText;
                if (magicianStateText == null) magicianStateText = gameConfig.robot1MagicianStateText;
            }
            else if (robotNumber == 2)
            {
                if (currentTaskText == null) currentTaskText = gameConfig.robot2CurrentTaskText;
                if (previousTasksText == null) previousTasksText = gameConfig.robot2PreviousTasksText;
                if (magicianStateText == null) magicianStateText = gameConfig.robot2MagicianStateText;
            }
        }
        
        if (rosConnector.RosSocket == null)
        {
            Debug.LogError($"TrajectoryEventSubscriber (Robot {robotNumber}): RosSocket null! RosConnector bağlantısını kontrol edin.");
            return;
        }
        
        // RosConnector'dan RosSocket'i al ve topic'e abone ol
        // Not: TrajectoryExecutionEvent mesaj tipi RosSharp'da olmayabilir, bu durumda String kullanabiliriz
        try
        {
            // Önce TrajectoryExecutionEvent'i deneyelim, yoksa String kullan
            rosConnector.RosSocket.Subscribe<String>(topicName, ReceiveTrajectoryEventMessage);
            isMessageReceived = false;
            Debug.Log($"TrajectoryEventSubscriber (Robot {robotNumber}): {topicName} topic'ine abone olundu.");
        }
        catch (System.Exception e)
        {
            Debug.LogWarning($"TrajectoryEventSubscriber: Topic'e abone olunurken hata (String mesaj tipi kullanılıyor): {e.Message}");
        }
        
        // İlk durumu göster
        UpdateUI();
    }

    // Bu fonksiyon, ROS'tan her yeni mesaj geldiğinde OTOMATİK olarak çalıştırılır
    private void ReceiveTrajectoryEventMessage(String message)
    {
        // Mesaj içeriğini parse et
        string eventData = message.data;
        
        // Mesaj formatına göre state'i belirle
        // Örnek: "EXECUTING", "SUCCEEDED", "FAILED", "IDLE" vb.
        if (!string.IsNullOrEmpty(eventData))
        {
            string upperData = eventData.ToUpper();
            
            if (upperData.Contains("EXECUTING") || upperData.Contains("RUNNING"))
            {
                currentState = "SENSING";
                currentTask = "Executing Trajectory";
            }
            else if (upperData.Contains("SUCCEEDED") || upperData.Contains("COMPLETED"))
            {
                // Önceki görevi previous tasks'a ekle
                if (!string.IsNullOrEmpty(currentTask) && currentTask != "IDLE")
                {
                    previousTasksQueue.Enqueue(currentTask);
                    while (previousTasksQueue.Count > maxPreviousTasks)
                    {
                        previousTasksQueue.Dequeue();
                    }
                }
                currentState = "IDLE";
                currentTask = "IDLE";
            }
            else if (upperData.Contains("FAILED") || upperData.Contains("ERROR"))
            {
                currentState = "ERROR FOUND";
                currentTask = "Trajectory Failed";
            }
            else if (upperData.Contains("IDLE") || upperData.Contains("READY"))
            {
                currentState = "IDLE";
                currentTask = "IDLE";
            }
            else
            {
                // Bilinmeyen durum, mesajı direkt kullan
                currentTask = eventData;
            }
        }
        
        isMessageReceived = true;
    }
        
    // Update fonksiyonu her frame'de çalışır, Unity'nin ana döngüsüdür.
    void Update()
    {
        // Eğer yeni bir mesaj geldiyse
        if(isMessageReceived)
        {
            UpdateUI();
            isMessageReceived = false; 
        }
    }
    
    private void UpdateUI()
    {
        // Current Task'ı güncelle
        if (currentTaskText != null)
        {
            currentTaskText.text = $"Current Task: {currentTask}";
            // Renk ayarı (yeşil)
            currentTaskText.color = Color.green;
        }
        
        // Previous Tasks'ı güncelle
        if (previousTasksText != null)
        {
            string previousTasksInfo = "Previous Tasks:\n";
            string[] tasksArray = previousTasksQueue.ToArray();
            for (int i = tasksArray.Length - 1; i >= 0; i--)
            {
                previousTasksInfo += $"{tasksArray[i]}\n";
            }
            previousTasksText.text = previousTasksInfo;
            // Renk ayarı (kırmızı/turuncu)
            previousTasksText.color = new Color(1f, 0.5f, 0f); // Turuncu
        }
        
        // MAGICIAN State'i güncelle
        if (magicianStateText != null)
        {
            string stateInfo = "States:\n";
            
            // Durumları renkli göster
            if (currentState == "IDLE")
            {
                stateInfo += "<color=green>IDLE</color>\n";
            }
            else if (currentState == "SENSING")
            {
                stateInfo += "<color=orange>SENSING</color>\n";
            }
            else if (currentState == "ERROR FOUND")
            {
                stateInfo += "<color=red>ERROR FOUND</color>\n";
            }
            else
            {
                stateInfo += $"{currentState}\n";
            }
            
            magicianStateText.text = stateInfo;
        }
    }
    
    // Manuel olarak state değiştirmek için public metodlar
    public void SetState(string state)
    {
        currentState = state;
        UpdateUI();
    }
    
    public void SetCurrentTask(string task)
    {
        currentTask = task;
        UpdateUI();
    }
    
    void OnDestroy()
    {
        previousTasksQueue.Clear();
    }
    
    // Factory için
    public class Factory : PlaceholderFactory<TrajectoryEventSubscriber> { }
}


