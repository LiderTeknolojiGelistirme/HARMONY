using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using Newtonsoft.Json;
using RosSharp.RosBridgeClient;
using UnityEngine;
using Zenject;

// System.String ile ROS# std_msgs/String aynı adı taşıdığı için takma ad kullanılır.
using RosString = RosSharp.RosBridgeClient.MessageTypes.Std.String;

/// <summary>
/// HARMONY senaryosundaki kusur (defect) tablosunu ROS'tan besleyip AR arayüzüne
/// sunar.
///
/// Abone olunan topic'ler:
///   /harmony/mock_perception/defect  — sensing sırasında kusur başına bir JSON
///   /harmony/defect_status           — cleaning sırasında durum güncellemeleri
///
/// NOT: /harmony/mock_perception/defect_list topic'i de aynı veriyi tek seferde
/// yayınlar, ancak TRANSIENT_LOCAL (latched) QoS kullanır ve rosbridge'in bunu
/// istemciye aktardığı doğrulanmadı. Bu yüzden birincil kaynak tekil topic'tir;
/// liste topic'i yalnızca <see cref="subscribeToDefectList"/> ile açılırsa
/// yedek olarak dinlenir.
/// </summary>
public class HarmonyDefectSubscriber : MonoBehaviour
{
    [Inject] private RosConnector rosConnector;

    [Header("Topic Ayarları")]
    [Tooltip("Kusur başına tekil JSON yayını (std_msgs/String).")]
    [SerializeField] private string defectTopic = "/harmony/mock_perception/defect";

    [Tooltip("Kusur durumu güncellemeleri (std_msgs/String).")]
    [SerializeField] private string defectStatusTopic = "/harmony/defect_status";

    [Tooltip("Toplu kusur listesi. Latched QoS kullandığı için rosbridge üzerinden gelmeyebilir.")]
    [SerializeField] private string defectListTopic = "/harmony/mock_perception/defect_list";

    [Tooltip("Yedek liste topic'ine de abone olunsun mu?")]
    [SerializeField] private bool subscribeToDefectList = false;

    [Header("Hata Ayıklama")]
    [Tooltip("Gelen her mesajı konsola bas. AR cihazında kapalı tut.")]
    [SerializeField] private bool verboseLogging = false;

    // Kusurlar geliş sırasında tutulur (DEF-01 ... DEF-04). Sözlük hızlı erişim,
    // liste ise arayüzdeki satır sırasının korunması için.
    private readonly Dictionary<string, HarmonyDefect> defectsById =
        new Dictionary<string, HarmonyDefect>();
    private readonly List<HarmonyDefect> orderedDefects = new List<HarmonyDefect>();

    // ROS# geri çağrıları arka thread'den gelir; Unity API'sine oradan
    // dokunulamaz. Ham gövdeleri kuyruğa alıp Update() içinde işliyoruz.
    private readonly ConcurrentQueue<string> defectQueue = new ConcurrentQueue<string>();
    private readonly ConcurrentQueue<string> statusQueue = new ConcurrentQueue<string>();

    /// <summary>Yeni bir kusur tabloya eklendiğinde tetiklenir.</summary>
    public event Action<HarmonyDefect> OnDefectAdded;

    /// <summary>Mevcut bir kusurun durumu veya konumu değiştiğinde tetiklenir.</summary>
    public event Action<HarmonyDefect> OnDefectUpdated;

    /// <summary>Tablo temizlendiğinde (yeni tarama turu) tetiklenir.</summary>
    public event Action OnDefectsCleared;

    /// <summary>Geliş sırasına göre kusur listesi (salt okunur).</summary>
    public IReadOnlyList<HarmonyDefect> Defects => orderedDefects;

    /// <summary>Tablodaki toplam kusur sayısı.</summary>
    public int DefectCount => orderedDefects.Count;

    /// <summary>Durumu CLEANED olan kusur sayısı.</summary>
    public int CleanedCount
    {
        get
        {
            int count = 0;
            for (int i = 0; i < orderedDefects.Count; i++)
                if (orderedDefects[i].Status == DefectStatus.Cleaned) count++;
            return count;
        }
    }

    private void Start()
    {
        if (rosConnector == null)
        {
            Debug.LogError("HarmonyDefectSubscriber: RosConnector inject edilemedi! " +
                           "SceneContext ve GameInstaller kontrol edin.");
            return;
        }

        if (rosConnector.RosSocket == null)
        {
            Debug.LogError("HarmonyDefectSubscriber: RosSocket null! " +
                           "RosConnector bağlantısını kontrol edin.");
            return;
        }

        rosConnector.RosSocket.Subscribe<RosString>(defectTopic, ReceiveDefectMessage);
        rosConnector.RosSocket.Subscribe<RosString>(defectStatusTopic, ReceiveStatusMessage);

        string subscribed = $"{defectTopic}, {defectStatusTopic}";

        if (subscribeToDefectList)
        {
            rosConnector.RosSocket.Subscribe<RosString>(defectListTopic, ReceiveDefectListMessage);
            subscribed += $", {defectListTopic}";
        }

        Debug.Log($"HarmonyDefectSubscriber: abone olundu → {subscribed}");
    }

    // ---------------------------------------------------------------------
    // ROS geri çağrıları — ARKA THREAD. Sadece kuyruğa yazarlar.
    // ---------------------------------------------------------------------

    private void ReceiveDefectMessage(RosString message)
    {
        if (message?.data == null) return;
        defectQueue.Enqueue(message.data);
    }

    private void ReceiveStatusMessage(RosString message)
    {
        if (message?.data == null) return;
        statusQueue.Enqueue(message.data);
    }

    private void ReceiveDefectListMessage(RosString message)
    {
        if (message?.data == null) return;
        // Liste gövdesi JSON array'dir; tek tek ayrıştırma Update() içinde yapılır.
        defectQueue.Enqueue(message.data);
    }

    // ---------------------------------------------------------------------
    // Ana thread işlemesi
    // ---------------------------------------------------------------------

    private void Update()
    {
        while (defectQueue.TryDequeue(out string defectJson))
            ProcessDefectJson(defectJson);

        while (statusQueue.TryDequeue(out string statusJson))
            ProcessStatusJson(statusJson);
    }

    /// <summary>
    /// Tekil kusur gövdesini veya JSON array'i ayrıştırıp tabloya işler.
    /// Hangi biçimin geldiği ilk anlamlı karaktere bakılarak anlaşılır.
    /// </summary>
    /// <param name="json">Ham JSON metni.</param>
    private void ProcessDefectJson(string json)
    {
        try
        {
            string trimmed = json.TrimStart();

            if (trimmed.StartsWith("["))
            {
                var list = JsonConvert.DeserializeObject<List<HarmonyDefectDto>>(json);
                if (list == null) return;
                foreach (var dto in list) UpsertDefect(dto);
            }
            else
            {
                var dto = JsonConvert.DeserializeObject<HarmonyDefectDto>(json);
                UpsertDefect(dto);
            }
        }
        catch (JsonException e)
        {
            Debug.LogWarning($"HarmonyDefectSubscriber: kusur JSON'u ayrıştırılamadı — {e.Message}");
        }
    }

    /// <summary>
    /// Durum güncellemesi gövdesini ayrıştırıp ilgili kusura uygular.
    /// </summary>
    /// <param name="json">Ham JSON metni.</param>
    private void ProcessStatusJson(string json)
    {
        try
        {
            var dto = JsonConvert.DeserializeObject<HarmonyDefectStatusDto>(json);
            if (dto == null || string.IsNullOrEmpty(dto.DefectId)) return;

            string id = dto.DefectId.Trim();
            if (!defectsById.TryGetValue(id, out HarmonyDefect defect))
            {
                // Durum, kusurun kendisinden önce gelmiş olabilir; sessizce yok say.
                if (verboseLogging)
                    Debug.Log($"HarmonyDefectSubscriber: {id} için durum geldi ama kusur tabloda yok.");
                return;
            }

            DefectStatus newStatus = HarmonyDefect.ParseStatus(dto.Status);
            if (defect.Status == newStatus) return;

            defect.Status = newStatus;
            if (verboseLogging)
                Debug.Log($"HarmonyDefectSubscriber: {id} → {newStatus}");

            OnDefectUpdated?.Invoke(defect);
        }
        catch (JsonException e)
        {
            Debug.LogWarning($"HarmonyDefectSubscriber: durum JSON'u ayrıştırılamadı — {e.Message}");
        }
    }

    /// <summary>
    /// Kusuru tabloya ekler veya mevcut kaydı günceller.
    /// Durum bilgisi yalnızca kusur ilk kez eklenirken DTO'dan alınır; sonraki
    /// güncellemelerde /harmony/defect_status otoritedir.
    /// </summary>
    /// <param name="dto">Ayrıştırılmış kusur gövdesi.</param>
    private void UpsertDefect(HarmonyDefectDto dto)
    {
        if (dto == null || string.IsNullOrEmpty(dto.DefectId)) return;

        string id = dto.DefectId.Trim();

        if (defectsById.TryGetValue(id, out HarmonyDefect existing))
        {
            existing.UpdateGeometry(dto);
            OnDefectUpdated?.Invoke(existing);
            return;
        }

        HarmonyDefect defect = HarmonyDefect.FromDto(dto);
        if (defect == null) return;

        defectsById[id] = defect;
        orderedDefects.Add(defect);

        if (verboseLogging)
            Debug.Log($"HarmonyDefectSubscriber: kusur eklendi {id} " +
                      $"({defect.DefectType}) ROS={defect.RosPosition} Unity={defect.UnityPosition}");

        OnDefectAdded?.Invoke(defect);
    }

    /// <summary>
    /// Kusur tablosunu boşaltır. Yeni bir tarama turu (START komutu) başlatılmadan
    /// önce çağrılmalıdır; aksi halde önceki turun kusurları listede kalır.
    /// </summary>
    public void Clear()
    {
        defectsById.Clear();
        orderedDefects.Clear();

        // Kuyrukta bekleyen eski mesajlar yeni tura sızmasın.
        while (defectQueue.TryDequeue(out _)) { }
        while (statusQueue.TryDequeue(out _)) { }

        OnDefectsCleared?.Invoke();
    }

    /// <summary>
    /// Test/simülasyon amaçlı: ROS'tan gelmiş gibi kusur gövdesi besler.
    /// Gövde normal akışla aynı kuyruğa girer, aynı işleyiciden geçer.
    /// </summary>
    /// <param name="json">Kusur JSON'u (tekil nesne veya dizi).</param>
    public void InjectDefectJson(string json)
    {
        if (!string.IsNullOrEmpty(json)) defectQueue.Enqueue(json);
    }

    /// <summary>
    /// Test/simülasyon amaçlı: durum güncellemesi besler.
    /// </summary>
    /// <param name="json">Durum JSON'u.</param>
    public void InjectStatusJson(string json)
    {
        if (!string.IsNullOrEmpty(json)) statusQueue.Enqueue(json);
    }

    /// <summary>
    /// Kimliğe göre kusur arar.
    /// </summary>
    /// <param name="defectId">Kusur kimliği, örn. "DEF-02".</param>
    /// <returns>Bulunursa kusur, bulunamazsa null.</returns>
    public HarmonyDefect GetDefect(string defectId)
    {
        if (string.IsNullOrEmpty(defectId)) return null;
        defectsById.TryGetValue(defectId.Trim(), out HarmonyDefect defect);
        return defect;
    }
}
