using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.XR.ARFoundation;
using UnityEngine.XR.ARSubsystems;

/// <summary>
/// ROS'tan gelen kusurları, dört spatial anchor'ın tanımladığı dikdörtgen
/// yüzeyde fiziksel karşılıklarına oturtur.
///
/// Çalışma mantığı iki dikdörtgen arasında eşleme kurmaktır:
///   1. ROS tarafındaki kapı yüzeyi — dört köşesi world frame'de metre
///      cinsinden tanımlıdır (<see cref="rosTopLeft"/> vb.).
///   2. AR tarafındaki yüzey — <see cref="SpatialAnchorsExample"/>'ın
///      yerleştirdiği dört anchor.
///
/// Kusurun ROS konumu birinci dikdörtgene izdüşürülerek normalize (u,v)
/// koordinatına çevrilir, sonra aynı (u,v) ikinci dikdörtgende bilinear
/// interpolasyonla noktaya dönüşür. Bu yaklaşım düzlem tam eksen hizalı
/// olmasa da doğru sonuç verir, çünkü izdüşüm kenar vektörleri üzerinden
/// yapılır.
/// </summary>
public class HarmonyDefectSurfaceMapper : MonoBehaviour
{
    // Durum renkleri — harmony_defects.py içindeki _STATUS_COLORS ile aynı
    // olmalıdır; operatör RViz ile gözlük arasında geçerken şaşırmasın.
    private static readonly Color ColCleaning = new Color32(0xff, 0xe5, 0x1a, 255);
    private static readonly Color ColCleaned  = new Color32(0x26, 0xcc, 0x4d, 255);
    private static readonly Color ColFailed   = new Color32(0xcc, 0x33, 0xe5, 255);
    private static readonly Color ColUnknown  = new Color32(0x94, 0xa3, 0xb8, 255);

    // Tür renkleri — yalnızca kusur DETECTED iken geçerli; durum rengi gelirse
    // o baskındır. harmony_defects.py içindeki _TYPE_COLORS ile aynıdır.
    private static readonly Color ColProtrusion = new Color32(0xe5, 0x26, 0x26, 255);
    private static readonly Color ColDent       = new Color32(0xff, 0x8c, 0x00, 255);

    [Header("Kaynaklar")]
    [Tooltip("Kusur akışı. Boş bırakılırsa sahnede aranır.")]
    [SerializeField] private HarmonyDefectSubscriber defectSubscriber;

    [Tooltip("Köşe anchor'larını yöneten bileşen. Boş bırakılırsa sahnede aranır.")]
    [SerializeField] private SpatialAnchorsExample anchorsExample;

    [Header("ROS Kapı Yüzeyi (world frame, metre)")]
    [Tooltip("Kapının sol-üst köşesi.")]
    [SerializeField] private Vector3 rosTopLeft = new Vector3(-1.0f, 0.15f, 1.65f);

    [Tooltip("Kapının sağ-üst köşesi.")]
    [SerializeField] private Vector3 rosTopRight = new Vector3(-1.0f, 1.80f, 1.65f);

    [Tooltip("Kapının sağ-alt köşesi.")]
    [SerializeField] private Vector3 rosBottomRight = new Vector3(-1.0f, 1.80f, 0.90f);

    [Tooltip("Kapının sol-alt köşesi.")]
    [SerializeField] private Vector3 rosBottomLeft = new Vector3(-1.0f, 0.15f, 0.90f);

    [Header("Kalibrasyon")]
    [Tooltip("Yatay eksen ters çalışıyorsa işaretleyin (kusurlar sağ-sol yer değiştirmiş görünüyorsa).")]
    [SerializeField] private bool flipU;

    [Tooltip("Dikey eksen ters çalışıyorsa işaretleyin (kusurlar alt-üst ters görünüyorsa).")]
    [SerializeField] private bool flipV;

    [Tooltip("Yüzey dikdörtgeni dışına düşen kusurları kenara sıkıştır. " +
             "Kapalıysa dışarı taşan kusurlar da hesaplandığı yerde gösterilir.")]
    [SerializeField] private bool clampToSurface = true;

    [Tooltip("ROS dikdörtgenini elle tanımlamak yerine gelen kusurların " +
             "sınır kutusuna oturt. Kapı ölçüleri bilinmiyorken hızlı deneme için.")]
    [SerializeField] private bool fitToDefects;

    [Header("İşaretçi")]
    [Tooltip("Kullanılacak işaretçi ön yapısı. Boşsa çalışma zamanında küre üretilir.")]
    [SerializeField] private GameObject markerPrefab;

    [Tooltip("İşaretçi çapı [m].")]
    [SerializeField] private float markerSize = 0.05f;

    [Tooltip("İşaretçiyi yüzeyden bu kadar öne al; yüzeyle z-fighting olmasın.")]
    [SerializeField] private float surfaceOffset = 0.01f;

    [Tooltip("İşaretçinin yanında kusur özelliklerini gösteren baloncuğu çiz.")]
    [SerializeField] private bool showLabels = true;

    [Tooltip("Etiket yazı boyutu [m].")]
    [SerializeField] private float labelSize = 0.03f;

    [Header("Baloncuk")]
    [Tooltip("Panelin küreye göre konumu [m]. Çekme çizgisi küreden buraya uzanır.")]
    [SerializeField] private Vector3 calloutOffset = new Vector3(0.06f, 0.14f, 0f);

    [Tooltip("Panelin en küçük genişlik ve yüksekliği [m]. " +
             "Yazı sığmazsa panel bu değerin üzerine kendiliğinden büyür.")]
    [SerializeField] private Vector2 calloutPanelSize = new Vector2(0.16f, 0.09f);

    [Tooltip("Bir yazı satırının yüksekliği [m]. TextMeshPro'nun kendi fontSize " +
             "birimi buna eşit değildir; bileşen ölçüp kalibre eder.")]
    [SerializeField] private float calloutLineHeight = 0.018f;

    [Tooltip("Çekme çizgisi ve panel kenarlığı kalınlığı [m].")]
    [SerializeField] private float calloutLineWidth = 0.004f;

    [Tooltip("Panel kameraya dönsün. Kapatılırsa kapı düzlemine sabit kalır.")]
    [SerializeField] private bool calloutFacesCamera = true;

    [Tooltip("Paneller arasında bırakılacak en az boşluk [m]. " +
             "Kusurlar birbirine yakınken baloncuklar bu payı koruyacak şekilde dağıtılır.")]
    [SerializeField] private float calloutSpacing = 0.012f;

    [Tooltip("Yerleşimin saniyede kaç kez gözden geçirileceği. " +
             "Düşük değer, kullanıcı başını çevirirken panellerin yer değiştirmesini azaltır.")]
    [SerializeField] private float calloutLayoutRateHz = 4f;

    [Header("Hata Ayıklama")]
    [Tooltip("Hesaplanan (u,v) değerlerini konsola bas.")]
    [SerializeField] private bool verboseLogging;

    /// <summary>
    /// Baloncuk yerleşim yuvaları: yapılandırılan yönün aynaları.
    /// 0 numaralı yuva her zaman <see cref="calloutOffset"/>'in kendisidir,
    /// böylece kusurlar birbirinden uzakken görünüm değişmez.
    /// </summary>
    private static readonly Vector2[] CalloutMirrors =
    {
        new Vector2(1f, 1f),    // yapılandırılan yön
        new Vector2(-1f, 1f),   // sola aynala
        new Vector2(1f, -1f),   // aşağı aynala
        new Vector2(-1f, -1f),  // sol-aşağı
    };

    /// <summary>Aynalar yetmezse baloncuk küreden bu katlarda uzaklaştırılır.</summary>
    private static readonly float[] CalloutDistances = { 1f, 1.7f, 2.4f };

    /// <summary>Kusur kimliği → sahnedeki işaretçi.</summary>
    private readonly Dictionary<string, GameObject> markers = new Dictionary<string, GameObject>();

    /// <summary>Yerleşim hesabında kullanılan geçici tamponlar.</summary>
    private readonly List<string> layoutOrder = new List<string>();
    private readonly List<Vector2> placedCenters = new List<Vector2>();
    private readonly List<Vector2> placedExtents = new List<Vector2>();

    /// <summary>Yerleşimin en son ne zaman gözden geçirildiği.</summary>
    private float layoutTimer;

    /// <summary>Kusur kimliği → normalize yüzey koordinatı.</summary>
    private readonly Dictionary<string, Vector2> uvByDefect = new Dictionary<string, Vector2>();

    private bool markersHidden;

    private void Start()
    {
        if (defectSubscriber == null) defectSubscriber = FindObjectOfType<HarmonyDefectSubscriber>();
        if (anchorsExample == null)
            anchorsExample = FindObjectOfType<SpatialAnchorsExample>(true);

        if (defectSubscriber == null)
        {
            Debug.LogError("HarmonyDefectSurfaceMapper: HarmonyDefectSubscriber bulunamadı!");
            return;
        }

        if (anchorsExample == null)
            Debug.LogWarning("HarmonyDefectSurfaceMapper: SpatialAnchorsExample bulunamadı; " +
                             "yüzey hazır olana kadar işaretçiler gizli kalacak.");

        defectSubscriber.OnDefectAdded += HandleDefectAdded;
        defectSubscriber.OnDefectUpdated += HandleDefectUpdated;
        defectSubscriber.OnDefectsCleared += HandleDefectsCleared;
    }

    private void OnDestroy()
    {
        if (defectSubscriber == null) return;
        defectSubscriber.OnDefectAdded -= HandleDefectAdded;
        defectSubscriber.OnDefectUpdated -= HandleDefectUpdated;
        defectSubscriber.OnDefectsCleared -= HandleDefectsCleared;
    }

    // =====================================================================
    // Kusur olayları
    // =====================================================================

    private void HandleDefectAdded(HarmonyDefect defect)
    {
        if (fitToDefects) RecomputeAllUv();
        else uvByDefect[defect.Id] = ProjectToUv(defect.RosPosition);

        EnsureMarker(defect);
    }

    private void HandleDefectUpdated(HarmonyDefect defect)
    {
        if (fitToDefects) RecomputeAllUv();
        else uvByDefect[defect.Id] = ProjectToUv(defect.RosPosition);

        EnsureMarker(defect);
        ApplyStatusColor(defect);
    }

    private void HandleDefectsCleared()
    {
        foreach (var go in markers.Values)
            if (go != null) Destroy(go);

        markers.Clear();
        uvByDefect.Clear();
    }

    // =====================================================================
    // Yüzey eşleme
    // =====================================================================

    private void LateUpdate()
    {
        List<ARAnchor> corners = (anchorsExample != null && anchorsExample.SurfaceReady)
            ? anchorsExample.GetOrderedCornersForMapping()
            : null;

        if (corners == null || corners.Count < 4 || !AllCornersUsable(corners))
        {
            SetMarkersVisible(false);
            return;
        }

        SetMarkersVisible(true);

        // c0=sol-üst, c1=sağ-üst, c2=sağ-alt, c3=sol-alt (SpatialAnchorsExample sırası)
        Vector3 c0 = corners[0].transform.position;
        Vector3 c1 = corners[1].transform.position;
        Vector3 c2 = corners[2].transform.position;
        Vector3 c3 = corners[3].transform.position;

        Vector3 normal = Vector3.Cross(c2 - c0, c3 - c1).normalized;

        foreach (var pair in markers)
        {
            if (pair.Value == null) continue;
            if (!uvByDefect.TryGetValue(pair.Key, out Vector2 uv)) continue;

            // BilinearPoint'te v aşağıya doğru arttığı için ters çeviriyoruz.
            Vector3 point = BilinearPoint(c0, c1, c2, c3, uv.x, 1f - uv.y);
            pair.Value.transform.position = point + normal * surfaceOffset;

            // Etiket her zaman kullanıcıya dönük dursun.
            if (Camera.main != null)
                pair.Value.transform.rotation =
                    Quaternion.LookRotation(pair.Value.transform.position - Camera.main.transform.position);
        }

        // İşaretçiler yerine oturduktan sonra baloncukları çakışmayacak
        // şekilde dağıt; konumlar bu noktada kesinleşmiş olur.
        UpdateCalloutLayout();
    }

    /// <summary>
    /// ROS world frame'indeki herhangi bir noktayı AR sahnesindeki karşılığına
    /// çevirir.
    ///
    /// Kusur işaretçileri yüzeye yapıştığı için bilinear eşleme kullanır; ancak
    /// yörünge gibi yüzeyden ayrık noktalar için düzlem tabanlı katı dönüşüm
    /// gerekir. Burada ROS kapı düzleminin (sağ, yukarı, normal) çatısı ile
    /// anchor düzleminin çatısı arasında dönüşüm kurulur; yüzeye olan uzaklık
    /// (normal bileşeni) korunur.
    /// </summary>
    /// <param name="rosPosition">ROS world frame konumu [m].</param>
    /// <param name="arPosition">AR sahnesindeki karşılığı.</param>
    /// <returns>Yüzey hazır ve dönüşüm kurulabildiyse true.</returns>
    public bool TryRosToAr(Vector3 rosPosition, out Vector3 arPosition)
    {
        arPosition = Vector3.zero;

        List<ARAnchor> corners = (anchorsExample != null && anchorsExample.SurfaceReady)
            ? anchorsExample.GetOrderedCornersForMapping()
            : null;

        if (corners == null || corners.Count < 4 || !AllCornersUsable(corners))
            return false;

        // c0=sol-üst, c1=sağ-üst, c2=sağ-alt, c3=sol-alt
        Vector3 arOrigin = corners[3].transform.position;
        Vector3 arRight = corners[2].transform.position - arOrigin;
        Vector3 arUp = corners[0].transform.position - arOrigin;

        Vector3 rosOrigin = rosBottomLeft;
        Vector3 rosRight = rosBottomRight - rosBottomLeft;
        Vector3 rosUp = rosTopLeft - rosBottomLeft;

        float rosRightLen = rosRight.magnitude;
        float rosUpLen = rosUp.magnitude;
        float arRightLen = arRight.magnitude;
        float arUpLen = arUp.magnitude;

        if (rosRightLen < 1e-6f || rosUpLen < 1e-6f || arRightLen < 1e-6f || arUpLen < 1e-6f)
            return false;

        Vector3 rosRightN = rosRight / rosRightLen;
        Vector3 rosUpN = rosUp / rosUpLen;
        Vector3 rosNormalN = Vector3.Cross(rosRightN, rosUpN).normalized;

        Vector3 arRightN = arRight / arRightLen;
        Vector3 arUpN = arUp / arUpLen;
        Vector3 arNormalN = Vector3.Cross(arRightN, arUpN).normalized;

        Vector3 local = rosPosition - rosOrigin;
        float a = Vector3.Dot(local, rosRightN);
        float b = Vector3.Dot(local, rosUpN);
        float c = Vector3.Dot(local, rosNormalN);

        // Anchor dikdörtgeni ROS dikdörtgeninden farklı ölçekte kurulmuş
        // olabilir; her eksen kendi oranıyla ölçeklenir, derinlik için ikisinin
        // ortalaması kullanılır.
        float sx = arRightLen / rosRightLen;
        float sy = arUpLen / rosUpLen;
        float sz = (sx + sy) * 0.5f;

        if (flipU) a = rosRightLen - a;
        if (flipV) b = rosUpLen - b;

        arPosition = arOrigin
                   + arRightN * (a * sx)
                   + arUpN * (b * sy)
                   + arNormalN * (c * sz);
        return true;
    }

    /// <summary>
    /// ROS konumunu kapı dikdörtgenine izdüşürüp normalize (u,v) verir.
    /// u soldan sağa, v alttan üste artar.
    /// </summary>
    /// <param name="rosPosition">Kusurun world frame konumu [m].</param>
    private Vector2 ProjectToUv(Vector3 rosPosition)
    {
        Vector3 origin = rosBottomLeft;
        Vector3 right = rosBottomRight - rosBottomLeft;
        Vector3 up = rosTopLeft - rosBottomLeft;

        float rightLenSq = right.sqrMagnitude;
        float upLenSq = up.sqrMagnitude;

        if (rightLenSq < 1e-6f || upLenSq < 1e-6f)
        {
            Debug.LogWarning("HarmonyDefectSurfaceMapper: ROS kapı köşeleri geçersiz " +
                             "(kenar uzunluğu sıfır). Inspector'daki köşeleri kontrol edin.");
            return new Vector2(0.5f, 0.5f);
        }

        Vector3 local = rosPosition - origin;
        float u = Vector3.Dot(local, right) / rightLenSq;
        float v = Vector3.Dot(local, up) / upLenSq;

        if (flipU) u = 1f - u;
        if (flipV) v = 1f - v;

        if (clampToSurface)
        {
            u = Mathf.Clamp01(u);
            v = Mathf.Clamp01(v);
        }

        if (verboseLogging)
            Debug.Log($"HarmonyDefectSurfaceMapper: ROS{rosPosition} → uv({u:F3}, {v:F3})");

        return new Vector2(u, v);
    }

    /// <summary>
    /// <see cref="fitToDefects"/> açıkken ROS dikdörtgenini gelen kusurların
    /// sınır kutusuna oturtup tüm (u,v) değerlerini yeniden hesaplar.
    /// </summary>
    private void RecomputeAllUv()
    {
        if (defectSubscriber == null || defectSubscriber.DefectCount == 0) return;

        var defects = defectSubscriber.Defects;

        // Kapı düzlemi ROS'ta Y (yatay) ve Z (dikey) ekseninde uzanıyor.
        float minY = float.MaxValue, maxY = float.MinValue;
        float minZ = float.MaxValue, maxZ = float.MinValue;

        for (int i = 0; i < defects.Count; i++)
        {
            Vector3 p = defects[i].RosPosition;
            if (p.y < minY) minY = p.y;
            if (p.y > maxY) maxY = p.y;
            if (p.z < minZ) minZ = p.z;
            if (p.z > maxZ) maxZ = p.z;
        }

        // Tek kusur varsa kutu sıfır genişlikte olur; küçük bir pay bırak.
        float padY = Mathf.Max((maxY - minY) * 0.15f, 0.05f);
        float padZ = Mathf.Max((maxZ - minZ) * 0.15f, 0.05f);
        minY -= padY; maxY += padY;
        minZ -= padZ; maxZ += padZ;

        uvByDefect.Clear();
        for (int i = 0; i < defects.Count; i++)
        {
            Vector3 p = defects[i].RosPosition;
            float u = Mathf.InverseLerp(minY, maxY, p.y);
            float v = Mathf.InverseLerp(minZ, maxZ, p.z);
            if (flipU) u = 1f - u;
            if (flipV) v = 1f - v;
            uvByDefect[defects[i].Id] = new Vector2(u, v);
        }
    }

    // =====================================================================
    // İşaretçiler
    // =====================================================================

    /// <summary>
    /// Kusur için işaretçi yoksa oluşturur.
    /// </summary>
    /// <param name="defect">Kusur kaydı.</param>
    private void EnsureMarker(HarmonyDefect defect)
    {
        if (markers.ContainsKey(defect.Id)) return;

        GameObject go;

        if (markerPrefab != null)
        {
            go = Instantiate(markerPrefab, transform);
        }
        else
        {
            go = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            go.transform.SetParent(transform, false);

            // Çarpışma gerekmez; XR ray'lerini engellemesin.
            var col = go.GetComponent<Collider>();
            if (col != null) Destroy(col);
        }

        go.name = "DefectMarker_" + defect.Id;
        go.transform.localScale = Vector3.one * markerSize;

        if (showLabels) CreateCallout(go);

        markers[defect.Id] = go;
        ApplyStatusColor(defect);
    }

    /// <summary>
    /// İşaretçiye çekme çizgisi ve özellik panelinden oluşan baloncuğu ekler.
    /// </summary>
    /// <param name="parent">Kusur küresi.</param>
    private void CreateCallout(GameObject parent)
    {
        var callout = parent.AddComponent<HarmonyDefectCallout>();

        // Küre markerSize kadar ölçeklendiği için baloncuk bu ölçeği geri alır;
        // böylece offset ve panel boyutu doğrudan metre cinsinden geçerlidir.
        float scaleCompensation = 1f / Mathf.Max(markerSize, 1e-4f);

        callout.Build(
            scaleCompensation,
            calloutOffset,
            calloutPanelSize,
            calloutLineHeight,
            calloutLineWidth,
            calloutFacesCamera);
    }

    // =====================================================================
    // Baloncuk yerleşimi
    // =====================================================================

    /// <summary>
    /// Birbirine yakın kusurların panellerini farklı yönlere dağıtır.
    ///
    /// İşaretçiler kameraya döndürüldüğü için baloncuk offset'inin x/y'si
    /// doğrudan ekran sağı/yukarısına karşılık gelir; çakışma sınaması da bu
    /// düzlemde, yani kullanıcının gerçekten gördüğü yerleşim üzerinde yapılır.
    ///
    /// Yerleşim yapışkandır: bir panelin mevcut yönü hâlâ çakışmıyorsa
    /// korunur. Kimliğe göre sabit sıra ve düşük tazeleme hızıyla birlikte bu,
    /// kullanıcı başını çevirdikçe panellerin sürekli yer değiştirmesini önler.
    /// </summary>
    private void UpdateCalloutLayout()
    {
        if (!showLabels) return;

        Camera cam = Camera.main;
        if (cam == null) return;

        // Tazeleme hızını sınırla; her karede yeniden dağıtmak titremeye yol açar.
        layoutTimer += Time.deltaTime;
        float period = calloutLayoutRateHz > 0.01f ? 1f / calloutLayoutRateHz : 0f;
        if (period > 0f && layoutTimer < period) return;
        layoutTimer = 0f;

        Vector3 camRight = cam.transform.right;
        Vector3 camUp = cam.transform.up;

        // Sabit sıra: yerleşim sözlük sırasına göre değişmesin.
        layoutOrder.Clear();
        foreach (var key in markers.Keys) layoutOrder.Add(key);
        layoutOrder.Sort(string.CompareOrdinal);

        placedCenters.Clear();
        placedExtents.Clear();

        for (int i = 0; i < layoutOrder.Count; i++)
        {
            GameObject go = markers[layoutOrder[i]];
            if (go == null || !go.activeInHierarchy) continue;

            var callout = go.GetComponent<HarmonyDefectCallout>();
            if (callout == null) continue;

            Vector3 p = go.transform.position;
            var anchor = new Vector2(Vector3.Dot(p, camRight), Vector3.Dot(p, camUp));
            Vector2 extents = callout.PanelSize * 0.5f + Vector2.one * (calloutSpacing * 0.5f);

            // Önce mevcut yönü koru; hâlâ boştaysa hiçbir şey oynatma.
            Vector3 chosen = callout.Offset;
            bool placed = !OverlapsPlaced(anchor + new Vector2(chosen.x, chosen.y), extents);

            if (!placed)
            {
                int slotCount = CalloutMirrors.Length * CalloutDistances.Length;
                for (int s = 0; s < slotCount; s++)
                {
                    Vector3 candidate = SlotOffset(s, callout.PanelSize);
                    if (OverlapsPlaced(anchor + new Vector2(candidate.x, candidate.y), extents))
                        continue;

                    chosen = candidate;
                    placed = true;
                    break;
                }

                // Hiçbir yuva boş değilse en uzağa at; üst üste binme kalsa bile
                // panel küreden olabildiğince uzaklaşsın.
                if (!placed) chosen = SlotOffset(slotCount - 1, callout.PanelSize);
            }

            callout.SetOffset(chosen);
            placedCenters.Add(anchor + new Vector2(chosen.x, chosen.y));
            placedExtents.Add(extents);
        }
    }

    /// <summary>
    /// Yuva numarasını offset'e çevirir. Yuvalar önce aynalar, sonra artan
    /// uzaklıklar olarak sıralanır; böylece yakın kusurlar önce sağ/sol/alt
    /// yönlere dağılır, ancak yer kalmazsa küreden uzaklaştırılır.
    /// </summary>
    /// <param name="index">Yuva numarası.</param>
    /// <param name="panelSize">Panelin ölçüsü [m]; yatay ayırma buna göre yapılır.</param>
    private Vector3 SlotOffset(int index, Vector2 panelSize)
    {
        int mirror = index % CalloutMirrors.Length;
        int distance = Mathf.Min(index / CalloutMirrors.Length, CalloutDistances.Length - 1);

        if (mirror == 0 && distance == 0) return calloutOffset;

        Vector2 m = CalloutMirrors[mirror];
        float d = CalloutDistances[distance];

        // Sola aynalarken offset'in kendi büyüklüğünü kullanmak yetmez: paneller
        // genelde offset'ten çok daha geniştir, dolayısıyla ±offset.x ile iki
        // panel hiçbir zaman ayrışmaz. Karşı tarafa geçerken panel genişliğini
        // aşan bir mesafe kullanıyoruz.
        float x = m.x < 0f
            ? -Mathf.Max(Mathf.Abs(calloutOffset.x), panelSize.x * 1.15f)
            : calloutOffset.x;

        return new Vector3(x * d, calloutOffset.y * m.y * d, calloutOffset.z);
    }

    /// <summary>
    /// Verilen dikdörtgen, bu turda yerleştirilmiş panellerden biriyle kesişiyor mu?
    /// </summary>
    /// <param name="center">Panel merkezi (kamera düzleminde).</param>
    /// <param name="extents">Panelin yarı genişlik ve yüksekliği.</param>
    private bool OverlapsPlaced(Vector2 center, Vector2 extents)
    {
        for (int i = 0; i < placedCenters.Count; i++)
        {
            Vector2 delta = center - placedCenters[i];
            Vector2 limit = extents + placedExtents[i];

            if (Mathf.Abs(delta.x) < limit.x && Mathf.Abs(delta.y) < limit.y)
                return true;
        }
        return false;
    }

    /// <summary>
    /// İşaretçiyi kusurun tür ve durumuna göre renklendirir; aynı rengi
    /// baloncuğun çizgisine ve panel kenarlığına da uygular.
    /// Panel zemini ve yazı bilerek dışarıda bırakılır ki metin okunur kalsın.
    /// </summary>
    /// <param name="defect">Kusur kaydı.</param>
    private void ApplyStatusColor(HarmonyDefect defect)
    {
        if (!markers.TryGetValue(defect.Id, out GameObject go) || go == null) return;

        Color c = DefectToColor(defect);
        var callout = go.GetComponent<HarmonyDefectCallout>();

        foreach (var r in go.GetComponentsInChildren<Renderer>())
        {
            if (r.material == null) continue;

            // Baloncuğun kendi parçaları temayı SetTheme ile alır.
            if (callout != null && callout.Owns(r)) continue;

            r.material.color = c;

            // Standart shader'da parlama varsa görünürlüğü artırır.
            if (r.material.HasProperty("_EmissionColor"))
            {
                r.material.EnableKeyword("_EMISSION");
                r.material.SetColor("_EmissionColor", c * 0.6f);
            }
        }

        if (callout != null)
        {
            callout.SetTheme(c);
            callout.SetContent(defect);
        }
    }

    /// <summary>
    /// Kusurun rengini belirler. Durum rengi türün önüne geçer: temizleniyor,
    /// temizlendi ve başarısız halleri türden bağımsız olarak ayırt edilmelidir.
    /// </summary>
    /// <param name="defect">Kusur kaydı.</param>
    private static Color DefectToColor(HarmonyDefect defect)
    {
        switch (defect.Status)
        {
            case DefectStatus.Cleaning: return ColCleaning;
            case DefectStatus.Cleaned: return ColCleaned;
            case DefectStatus.Failed: return ColFailed;
            case DefectStatus.Detected: return TypeToColor(defect.DefectType);
            default: return ColUnknown;
        }
    }

    /// <summary>
    /// Kusur türünün rengini verir. Tanınmayan tür gri döner; senaryo dışı bir
    /// tür gelirse işaretçi kaybolmasın.
    /// </summary>
    /// <param name="defectType">Kusur türü, örn. "Protrusion".</param>
    private static Color TypeToColor(string defectType)
    {
        if (string.IsNullOrEmpty(defectType)) return ColUnknown;

        switch (defectType.Trim().ToLowerInvariant())
        {
            case "protrusion": return ColProtrusion;
            case "dent": return ColDent;
            default: return ColUnknown;
        }
    }

    private void SetMarkersVisible(bool visible)
    {
        if (markersHidden == !visible) return;
        markersHidden = !visible;

        foreach (var go in markers.Values)
            if (go != null) go.SetActive(visible);
    }

    /// <summary>
    /// Dört köşe anchor'ının da var ve izleniyor olduğunu doğrular.
    /// </summary>
    /// <param name="corners">Köşe anchor listesi.</param>
    private static bool AllCornersUsable(List<ARAnchor> corners)
    {
        for (int i = 0; i < corners.Count; i++)
        {
            if (corners[i] == null || corners[i].trackingState != TrackingState.Tracking)
                return false;
        }
        return true;
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

#if UNITY_EDITOR
    /// <summary>
    /// Sahne görünümünde ROS kapı dikdörtgenini ve kusurların yerlerini çizer.
    /// Köşe değerlerini ayarlarken görsel geri bildirim sağlar.
    /// </summary>
    private void OnDrawGizmosSelected()
    {
        Gizmos.color = Color.cyan;
        Gizmos.DrawLine(rosTopLeft, rosTopRight);
        Gizmos.DrawLine(rosTopRight, rosBottomRight);
        Gizmos.DrawLine(rosBottomRight, rosBottomLeft);
        Gizmos.DrawLine(rosBottomLeft, rosTopLeft);
    }
#endif
}
