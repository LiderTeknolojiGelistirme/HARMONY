using UnityEngine;
using UnityEngine.UI;
using RosSharp.RosBridgeClient;
using RosSharp.RosBridgeClient.MessageTypes.Sensor;
using Zenject;
using RosImage = RosSharp.RosBridgeClient.MessageTypes.Sensor.Image;

public class ImageSubscriber : MonoBehaviour
{
    [Inject] private RosConnector rosConnector;
    [Inject] private GameConfig gameConfig;
    
    [Header("Topic ve Görüntü Ayarları")]
    [Tooltip("ROS topic name for image subscription.")]
    [SerializeField] private string topicName = "/image_robot1";

    [Tooltip("Unity UI Image component to show the received image.")]
    [SerializeField] private UnityEngine.UI.Image uiImage;

    private Texture2D texture2D = null;
    private byte[] imageData = null;
    private int imageWidth;
    private int imageHeight;
    private string imageEncoding;
    private bool isMessageReceived;
    private TextureFormat currentTextureFormat;
    private Sprite currentSprite = null;

    void Start()
    {
        if (rosConnector == null)
        {
            Debug.LogError("ImageSubscriber: RosConnector inject edilemedi! SceneContext ve GameInstaller kontrol edin.");
            return;
        }
        
        // Topic adını belirle: önce SerializeField, sonra GameConfig, sonra varsayılan
        if (string.IsNullOrEmpty(topicName))
        {
            topicName = gameConfig != null ? gameConfig.imageTopicName : "/image_robot1";
        }
        
        // UI Image'i belirle: önce SerializeField, sonra GameConfig'den topic'e göre
        if (uiImage == null && gameConfig != null)
        {
            if (topicName == "/image_robot1")
                uiImage = gameConfig.robot1Image;
            else if (topicName == "/image_robot2")
                uiImage = gameConfig.robot2Image;
        }
        
        // UI Image zorunlu
        if (uiImage == null)
        {
            Debug.LogError($"ImageSubscriber: {topicName} için UI Image atanmamış! Inspector'dan veya GameConfig'den atayın.");
            return;
        }
        
        texture2D = null;
        isMessageReceived = false;
        
        if (rosConnector.RosSocket == null)
        {
            Debug.LogError("ImageSubscriber: RosSocket null! RosConnector bağlantısını kontrol edin.");
            return;
        }
        
        rosConnector.RosSocket.Subscribe<RosImage>(topicName, ReceiveImageMessage);
        Debug.Log($"ImageSubscriber: {topicName} topic'ine abone olundu.");
    }

    private void ReceiveImageMessage(RosImage message)
    {
        // Gelen mesajdaki verileri kendi değişkenlerimize alalım
        imageWidth = (int)message.width;
        imageHeight = (int)message.height;
        imageData = message.data;
        imageEncoding = message.encoding;
        
        isMessageReceived = true;
    }

    void Update()
    {
        if (isMessageReceived)
        {
            // Encoding'e göre texture formatını belirle
            TextureFormat textureFormat = GetTextureFormat(imageEncoding);
            int bytesPerPixel = GetBytesPerPixel(imageEncoding);
            
            // Beklenen veri boyutunu hesapla
            int expectedDataSize = imageWidth * imageHeight * bytesPerPixel;
            
            // Veri boyutu kontrolü
            if (imageData == null || imageData.Length < expectedDataSize)
            {
                Debug.LogError($"ImageSubscriber: Yetersiz veri! Beklenen: {expectedDataSize} byte, Gelen: {(imageData?.Length ?? 0)} byte. Encoding: {imageEncoding}, Boyut: {imageWidth}x{imageHeight}");
                isMessageReceived = false;
                return;
            }
            
            // Texture formatı veya boyutu değiştiyse yeniden oluştur
            if (texture2D == null || 
                texture2D.width != imageWidth || 
                texture2D.height != imageHeight || 
                currentTextureFormat != textureFormat)
            {
                if (texture2D != null)
                {
                    Destroy(texture2D);
                }
                texture2D = new Texture2D(imageWidth, imageHeight, textureFormat, false);
                currentTextureFormat = textureFormat;
            }
            
            // Eğer renk formatı BGR ise, Unity için RGB'ye çevir
            byte[] processedData = imageData;
            if (imageEncoding.Equals("bgr8", System.StringComparison.OrdinalIgnoreCase))
            {
                // Kopya oluştur ve kopyayı işle
                processedData = new byte[imageData.Length];
                System.Array.Copy(imageData, processedData, imageData.Length);
                BGRtoRGB(processedData);
            }

            // Ham görüntü verisini dokuya yükle
            texture2D.LoadRawTextureData(processedData);
            // Değişiklikleri uygula
            texture2D.Apply();
            
            // Eski sprite'ı temizle
            if (currentSprite != null)
            {
                Destroy(currentSprite);
            }
            
            // UI Image'e texture'ı ata
            currentSprite = Sprite.Create(texture2D, new Rect(0, 0, texture2D.width, texture2D.height), new Vector2(0.5f, 0.5f));
            uiImage.sprite = currentSprite;

            isMessageReceived = false;
        }
    }
    
    // Encoding'e göre TextureFormat belirle
    private TextureFormat GetTextureFormat(string encoding)
    {
        encoding = encoding.ToLower();
        
        switch (encoding)
        {
            case "rgb8":
            case "bgr8":
                return TextureFormat.RGB24;
            case "rgba8":
            case "bgra8":
                return TextureFormat.RGBA32;
            case "mono8":
                return TextureFormat.R8;
            case "mono16":
                return TextureFormat.R16;
            default:
                Debug.LogWarning($"ImageSubscriber: Bilinmeyen encoding formatı: {encoding}. RGB24 kullanılıyor.");
                return TextureFormat.RGB24;
        }
    }
    
    // Encoding'e göre pixel başına byte sayısını belirle
    private int GetBytesPerPixel(string encoding)
    {
        encoding = encoding.ToLower();
        
        switch (encoding)
        {
            case "rgb8":
            case "bgr8":
                return 3;
            case "rgba8":
            case "bgra8":
                return 4;
            case "mono8":
                return 1;
            case "mono16":
                return 2;
            default:
                return 3; // Varsayılan RGB
        }
    }

    // Byte dizisindeki BGR piksellerini RGB'ye çeviren fonksiyon
    private void BGRtoRGB(byte[] data)
    {
        for (int i = 0; i < data.Length; i += 3)
        {
            // Mavi ve Kırmızı kanalları yer değiştir
            byte temp = data[i];
            data[i] = data[i + 2];
            data[i + 2] = temp;
        }
    }
    
    void OnDestroy()
    {
        // Sprite'ı temizle
        if (currentSprite != null)
        {
            Destroy(currentSprite);
            currentSprite = null;
        }
        
        // Texture'ı temizle
        if (texture2D != null)
        {
            Destroy(texture2D);
            texture2D = null;
        }
    }
    
    // Factory için
    public class Factory : PlaceholderFactory<ImageSubscriber> { }
}