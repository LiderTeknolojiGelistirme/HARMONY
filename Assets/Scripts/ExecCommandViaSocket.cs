using RosSharp.RosBridgeClient;
using RosSharp.RosBridgeClient.MessageTypes.Std;
using UnityEngine;
using Zenject;

public class ExecCommandViaSocket : MonoBehaviour
{
    [Inject] private RosConnector rosConnector;
    [Inject] private GameConfig gameConfig;
    
    private RosSocket rosSocket;
    private string publicationId;
    private string topic;

    void Start()
    {
        // Temel inject kontrolleri
        if (rosConnector == null)
        {
            Debug.LogError("ExecCommandViaSocket: RosConnector inject edilemedi!");
            return;
        }
        
        topic = (gameConfig != null) ? gameConfig.execTopic : "/harmony/cmd_input";

        // İlk deneme: Eğer bağlantı şans eseri hemen hazirsa başlat
        InitializeRosSocket();
    }

    private bool InitializeRosSocket()
    {
        // Eğer zaten tanımlıysa true dön
        if (rosSocket != null && !string.IsNullOrEmpty(publicationId)) return true;

        // RosConnector null kontrolü ekle
        if (rosConnector == null)
        {
            Debug.LogWarning("[ExecCommandViaSocket] RosConnector henüz inject edilmedi!");
            return false;
        }

        // RosConnector üzerinden socket'i almayı dene
        if (rosConnector.RosSocket == null) return false;

        rosSocket = rosConnector.RosSocket;
        publicationId = rosSocket.Advertise<String>(topic);
        Debug.Log($"[ExecCommandViaSocket] Bağlantı sağlandı ve {topic} advertise edildi.");
        return true;
    }

    public void Send(string cmd)
    {
        // HATA ÇÖZÜMÜ: Göndermeden önce bağlantı var mı kontrol et, yoksa kurmaya çalış
        if (!InitializeRosSocket())
        {
            Debug.LogWarning("ROS Bağlantısı henüz hazır değil, komut gönderilemedi: " + cmd);
            return;
        }

        var msg = new String { data = cmd };
        
        // Ekstra güvenlik: publicationId null ise yine patlamasın
        if (!string.IsNullOrEmpty(publicationId))
        {
            rosSocket.Publish(publicationId, msg);
            Debug.Log($"[ROS#] Sent exec cmd: {cmd}");
        }
    }
    
    public class Factory : PlaceholderFactory<ExecCommandViaSocket> { }
}