using System.Collections.Concurrent;
using System.Collections.Generic;
using RosSharp.RosBridgeClient;
using TMPro;
using UnityEngine;
using UnityEngine.XR.ARFoundation;
using UnityEngine.XR.ARSubsystems;
using Zenject;
using MagicianDetection = RosSharp.RosBridgeClient.MessageTypes.MagicianVision.Detection;

/// <summary>
/// MAGICIAN görüntü sınıflandırıcısının /detections topic'inden
/// (magician_vision_classifier/Detection) gelen kusur tespitlerini dinler ve
/// SpatialAnchorsExample'daki 4 spatial anchor'ın tanımladığı dikdörtgen yüzey
/// üzerinde işaretçi olarak gösterir.
///
/// Eşleme: kusur merkezi (x + w/2, y + h/2) görüntü boyutuna bölünerek (u,v)
/// normalize edilir; (u,v) köşelere bilinear interpolasyonla oturtulur.
/// Köşe yerleştirme sırası kameradaki görüntüyle eşleşmelidir:
/// 1 = görüntü SOL-ÜST, 2 = SAĞ-ÜST, 3 = SAĞ-ALT, 4 = SOL-ALT.
/// Yön ters düşerse flipU / flipV ile düzeltilir.
/// </summary>
public class DefectSurfaceVisualizer : MonoBehaviour
{
    [Inject(Optional = true)] private RosConnector rosConnector;

    [Header("Bağlantılar")]
    [Tooltip("4 köşe anchor'ını yöneten SpatialAnchorsExample. Boşsa sahnede aranır.")]
    [SerializeField] private SpatialAnchorsExample anchorsExample;

    [Header("ROS Ayarları")]
    [Tooltip("Kusur tespit topic'i (magician_vision_classifier/Detection).")]
    [SerializeField] private string topicName = "/detections";

    [Tooltip("Sınıflandırıcı kamerasının görüntü genişliği (piksel) — u normalizasyonu için.")]
    [SerializeField] private int imageWidth = 640;

    [Tooltip("Sınıflandırıcı kamerasının görüntü yüksekliği (piksel) — v normalizasyonu için.")]
    [SerializeField] private int imageHeight = 480;

    [Tooltip("Bu olasılığın altındaki tespitler yok sayılır (0-1).")]
    [Range(0f, 1f)]
    [SerializeField] private float minProbability = 0f;

    [Header("Eşleme")]
    [Tooltip("u eksenini aynala (kamera görüntüsü ile köşe sırası ters düşerse).")]
    [SerializeField] private bool flipU = false;

    [Tooltip("v eksenini aynala.")]
    [SerializeField] private bool flipV = false;

    [Tooltip("İşaretçinin yüzey normali boyunca kaldırılma mesafesi (metre).")]
    [SerializeField] private float surfaceOffset = 0.01f;

    [Header("İşaretçi Görünümü")]
    [Tooltip("Boşsa collider'sız küre primitifi kullanılır.")]
    [SerializeField] private GameObject markerPrefab;

    [Tooltip("Küre işaretçinin çapı (metre).")]
    [SerializeField] private float markerDiameter = 0.03f;

    [Tooltip("0 = kalıcı; >0 ise bu süre boyunca tekrar rapor edilmeyen işaretçi silinir (saniye).")]
    [SerializeField] private float markerLifetimeSeconds = 0f;

    [Tooltip("Düşük olasılıklı tespit rengi.")]
    [SerializeField] private Color lowProbabilityColor = new Color(1f, 0.85f, 0.1f);

    [Tooltip("Yüksek olasılıklı tespit rengi.")]
    [SerializeField] private Color highProbabilityColor = new Color(1f, 0.15f, 0.15f);

    [Tooltip("Aynı anda tutulacak azami işaretçi sayısı (taşma koruması).")]
    [SerializeField] private int maxMarkers = 300;

    /// <summary> Tek bir kusur işaretçisinin durum kaydı. </summary>
    private class MarkerEntry
    {
        public GameObject markerObject;
        public float u;
        public float v;
        public float probability;
        public float lastSeenTime;
        public string label;
    }

    private readonly ConcurrentQueue<MagicianDetection> pendingDetections = new();
    private readonly Dictionary<string, MarkerEntry> markers = new();
    private readonly List<string> expiredKeys = new();
    private string subscriptionId;
    private bool markersVisible = true;
    private Shader markerShader;

    /// <summary>
    /// ROS bağlantısını ve anchor kaynağını çözer, /detections topic'ine abone olur.
    /// </summary>
    private void Start()
    {
        if (anchorsExample == null)
            anchorsExample = FindFirstObjectByType<SpatialAnchorsExample>(FindObjectsInactive.Include);
        if (anchorsExample == null)
            Debug.LogWarning("DefectSurfaceVisualizer: SpatialAnchorsExample bulunamadı; yüzey hazır olana kadar işaretçiler gizli kalacak.");

        if (rosConnector == null)
            rosConnector = FindFirstObjectByType<RosConnector>();
        if (rosConnector == null || rosConnector.RosSocket == null)
        {
            Debug.LogError("DefectSurfaceVisualizer: RosConnector/RosSocket yok! Abonelik yapılamadı.");
            return;
        }

        subscriptionId = rosConnector.RosSocket.Subscribe<MagicianDetection>(topicName, ReceiveDetection);
        Debug.Log($"DefectSurfaceVisualizer: {topicName} topic'ine abone olundu.");
    }

    /// <summary>
    /// Socket thread'inden gelen mesajı ana thread'de işlenmek üzere kuyruğa alır.
    /// </summary>
    private void ReceiveDetection(MagicianDetection message)
    {
        pendingDetections.Enqueue(message);
    }

    /// <summary>
    /// Kuyruğu boşaltır, işaretçi kayıtlarını günceller ve yüzey hazırsa
    /// işaretçileri köşe anchor'larının güncel pozisyonlarına oturtur.
    /// </summary>
    private void Update()
    {
        while (pendingDetections.TryDequeue(out MagicianDetection det))
            RegisterDetection(det);

        RemoveExpiredMarkers();

        List<ARAnchor> corners = anchorsExample != null && anchorsExample.SurfaceReady
            ? anchorsExample.GetOrderedCornersForMapping()
            : null;

        if (corners == null || corners.Count < 4 || !AllCornersUsable(corners))
        {
            SetMarkersVisible(false);
            return;
        }

        Vector3 c0 = corners[0].transform.position;
        Vector3 c1 = corners[1].transform.position;
        Vector3 c2 = corners[2].transform.position;
        Vector3 c3 = corners[3].transform.position;

        // Dikdörtgenin normali (köşegen çapraz çarpımı), yukarı yönlü olacak şekilde
        Vector3 normal = Vector3.Cross(c2 - c0, c3 - c1).normalized;
        if (Vector3.Dot(normal, Vector3.up) < 0f)
            normal = -normal;

        SetMarkersVisible(true);

        foreach (MarkerEntry entry in markers.Values)
        {
            if (entry.markerObject == null)
                continue;

            float u = flipU ? 1f - entry.u : entry.u;
            float v = flipV ? 1f - entry.v : entry.v;
            entry.markerObject.transform.position = BilinearPoint(c0, c1, c2, c3, u, v) + normal * surfaceOffset;
        }
    }

    /// <summary>
    /// Tespiti normalize (u,v) koordinatına çevirip işaretçi kaydını oluşturur/günceller.
    /// Tile ızgarası sabit olduğundan (x,y,class_name) üçlüsü kalıcı kimlik olarak kullanılır.
    /// </summary>
    private void RegisterDetection(MagicianDetection det)
    {
        if (det.probability < minProbability || imageWidth <= 0 || imageHeight <= 0)
            return;

        string key = det.x + "_" + det.y + "_" + det.class_name;
        float u = Mathf.Clamp01((det.x + det.w * 0.5f) / imageWidth);
        float v = Mathf.Clamp01((det.y + det.h * 0.5f) / imageHeight);
        string label = string.IsNullOrEmpty(det.class_name) ? det.type : det.class_name;

        if (markers.TryGetValue(key, out MarkerEntry entry))
        {
            entry.u = u;
            entry.v = v;
            entry.lastSeenTime = Time.time;
            if (det.probability > entry.probability)
            {
                entry.probability = det.probability;
                ApplyProbabilityColor(entry.markerObject, det.probability);
            }
            return;
        }

        if (markers.Count >= maxMarkers)
            return;

        entry = new MarkerEntry
        {
            u = u,
            v = v,
            probability = det.probability,
            lastSeenTime = Time.time,
            label = label,
            markerObject = CreateMarkerObject(key, det.probability, label)
        };
        markers[key] = entry;
    }

    /// <summary>
    /// Yaşam süresi dolan işaretçileri kaldırır (markerLifetimeSeconds > 0 ise).
    /// </summary>
    private void RemoveExpiredMarkers()
    {
        if (markerLifetimeSeconds <= 0f)
            return;

        expiredKeys.Clear();
        foreach (KeyValuePair<string, MarkerEntry> pair in markers)
        {
            if (Time.time - pair.Value.lastSeenTime > markerLifetimeSeconds)
                expiredKeys.Add(pair.Key);
        }

        foreach (string key in expiredKeys)
        {
            if (markers[key].markerObject != null)
                Destroy(markers[key].markerObject);
            markers.Remove(key);
        }
    }

    /// <summary>
    /// Tüm köşe anchor'larının var ve takipte olduğunu doğrular.
    /// </summary>
    private bool AllCornersUsable(List<ARAnchor> corners)
    {
        foreach (ARAnchor corner in corners)
        {
            if (corner == null || corner.trackingState != TrackingState.Tracking)
                return false;
        }
        return true;
    }

    /// <summary>
    /// Yeni işaretçi objesi üretir: prefab varsa onu, yoksa collider'sız küre kullanır.
    /// </summary>
    private GameObject CreateMarkerObject(string key, float probability, string label)
    {
        GameObject marker;
        if (markerPrefab != null)
        {
            marker = Instantiate(markerPrefab, transform);
        }
        else
        {
            marker = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            marker.transform.SetParent(transform, true);
            marker.transform.localScale = Vector3.one * markerDiameter;
            Collider col = marker.GetComponent<Collider>();
            if (col != null)
                Destroy(col);
        }

        marker.name = "Defect_" + key;
        marker.SetActive(markersVisible);
        ApplyProbabilityColor(marker, probability);

        // Prefab içinde TMP etiketi varsa sınıf adı + olasılık yaz
        TMP_Text text = marker.GetComponentInChildren<TMP_Text>(true);
        if (text != null)
            text.text = label + " " + probability.ToString("P0");

        return marker;
    }

    /// <summary>
    /// Olasılığa göre düşük→yüksek renk geçişini işaretçinin materyaline uygular.
    /// </summary>
    private void ApplyProbabilityColor(GameObject marker, float probability)
    {
        if (marker == null)
            return;

        Color color = Color.Lerp(lowProbabilityColor, highProbabilityColor, Mathf.Clamp01(probability));
        Renderer[] renderers = marker.GetComponentsInChildren<Renderer>(true);
        foreach (Renderer rend in renderers)
        {
            if (rend.sharedMaterial == null || rend.sharedMaterial.shader == null || !rend.sharedMaterial.shader.isSupported)
            {
                Shader shader = GetMarkerShader();
                if (shader != null)
                    rend.material = new Material(shader);
            }

            if (rend.material != null)
            {
                if (rend.material.HasProperty("_BaseColor"))
                    rend.material.SetColor("_BaseColor", color);
                if (rend.material.HasProperty("_Color"))
                    rend.material.SetColor("_Color", color);
            }
        }
    }

    /// <summary>
    /// URP öncelikli, geriye dönük uyumlu shader arama zinciri.
    /// </summary>
    private Shader GetMarkerShader()
    {
        if (markerShader != null)
            return markerShader;

        string[] shaderNames =
        {
            "Universal Render Pipeline/Lit",
            "Universal Render Pipeline/Unlit",
            "Standard",
            "Unlit/Color"
        };

        foreach (string shaderName in shaderNames)
        {
            markerShader = Shader.Find(shaderName);
            if (markerShader != null)
                break;
        }
        return markerShader;
    }

    /// <summary>
    /// (u,v) koordinatını dört köşeli yüzeye bilinear interpolasyonla oturtur.
    /// c0=sol-üst, c1=sağ-üst, c2=sağ-alt, c3=sol-alt; u sağa, v aşağıya artar.
    /// </summary>
    private static Vector3 BilinearPoint(Vector3 c0, Vector3 c1, Vector3 c2, Vector3 c3, float u, float v)
    {
        Vector3 topEdge = Vector3.Lerp(c0, c1, u);
        Vector3 bottomEdge = Vector3.Lerp(c3, c2, u);
        return Vector3.Lerp(topEdge, bottomEdge, v);
    }

    /// <summary>
    /// Tüm işaretçilerin görünürlüğünü topluca değiştirir.
    /// </summary>
    private void SetMarkersVisible(bool visible)
    {
        if (markersVisible == visible)
            return;

        markersVisible = visible;
        foreach (MarkerEntry entry in markers.Values)
        {
            if (entry.markerObject != null)
                entry.markerObject.SetActive(visible);
        }
    }

    /// <summary>
    /// Tüm işaretçileri siler (ör. harita değişiminde UI'dan çağrılabilir).
    /// </summary>
    public void ClearMarkers()
    {
        foreach (MarkerEntry entry in markers.Values)
        {
            if (entry.markerObject != null)
                Destroy(entry.markerObject);
        }
        markers.Clear();
    }

    /// <summary>
    /// Aboneliği kapatır ve işaretçileri temizler.
    /// </summary>
    private void OnDestroy()
    {
        if (rosConnector != null && rosConnector.RosSocket != null && !string.IsNullOrEmpty(subscriptionId))
        {
            try { rosConnector.RosSocket.Unsubscribe(subscriptionId); }
            catch (System.Exception e) { Debug.LogWarning("DefectSurfaceVisualizer: Unsubscribe hatası: " + e.Message); }
        }
        ClearMarkers();
    }
}