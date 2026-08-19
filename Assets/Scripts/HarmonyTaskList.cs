using System.Collections.Generic;
using System.Text;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>Bir görev satırının durumu.</summary>
public enum HarmonyTaskState
{
    /// <summary>Henüz sırası gelmedi.</summary>
    Pending,

    /// <summary>Şu anda yürütülüyor.</summary>
    InProgress,

    /// <summary>Tamamlandı.</summary>
    Completed,

    /// <summary>Görev iptal edildi (STOP) ya da tur yeniden başlatıldı.</summary>
    Aborted
}

/// <summary>
/// Senaryonun görev listesini gösterir: tarama bakış noktaları (WP-00…) ve
/// giderilecek kusurlar (DEF-01…). Her satır bekliyor / yürütülüyor /
/// tamamlandı durumlarından birinde olur.
///
/// Satırlar ayrı nesneler yerine tek bir <see cref="TMP_Text"/> içinde
/// &lt;pos=%&gt; etiketiyle sütunlanır; <see cref="HarmonyMasterPanel"/>'deki
/// kusur tablosuyla aynı yöntem. Böylece liste uzunluğu değiştiğinde nesne
/// yaratıp yok etmek gerekmez.
///
/// Bileşen yalnızca gösterimden sorumludur; sırayı ve zamanlamayı
/// <see cref="HarmonyDemoScenario"/> (veya ROS akışı) belirler.
/// </summary>
public class HarmonyTaskList : MonoBehaviour
{
    // Sütun konumları. Başlık satırı sahnede aynı yüzdelerle tanımlıdır.
    private const string ColId = "<pos=0%>";
    private const string ColLabel = "<pos=22%>";
    private const string ColState = "<pos=72%>";

    /// <summary>Listedeki tek bir görev.</summary>
    public class Task
    {
        /// <summary>Kısa kimlik, örn. "WP-03" veya "DEF-02".</summary>
        public string Id;

        /// <summary>Açıklama metni.</summary>
        public string Label;

        /// <summary>Güncel durum.</summary>
        public HarmonyTaskState State;
    }

    [Header("UI Alanları")]
    [Tooltip("Görev satırlarının yazıldığı metin alanı.")]
    [SerializeField] private TMP_Text listText;

    [Tooltip("Başlıktaki ilerleme sayacı, örn. \"4 / 14 completed\".")]
    [SerializeField] private TMP_Text progressText;

    [Tooltip("İlerleme çubuğunun dolan kısmı. Genişliği oranla ölçeklenir.")]
    [SerializeField] private RectTransform progressFill;

    [Header("Görünüm")]
    [Tooltip("Liste boşken gösterilecek metin.")]
    [SerializeField] private string emptyMessage =
        "<i><color=#64748b>No tasks yet. Press START SCAN.</color></i>";

    private readonly List<Task> tasks = new List<Task>();
    private readonly Dictionary<string, Task> tasksById = new Dictionary<string, Task>();
    private readonly StringBuilder builder = new StringBuilder(1024);

    // İlerleme çubuğunun boş haldeki tam genişliği; ilk karede okunur.
    private float progressFullWidth = -1f;

    private bool needsRedraw = true;

    /// <summary>Listedeki görev sayısı.</summary>
    public int TaskCount => tasks.Count;

    /// <summary>Tamamlanan görev sayısı.</summary>
    public int CompletedCount
    {
        get
        {
            int n = 0;
            for (int i = 0; i < tasks.Count; i++)
                if (tasks[i].State == HarmonyTaskState.Completed) n++;
            return n;
        }
    }

    private void Awake()
    {
        if (progressFill != null) progressFullWidth = progressFill.rect.width;
    }

    private void LateUpdate()
    {
        // Aynı karede birden çok görev değişebilir; tabloyu bir kez çiziyoruz.
        if (!needsRedraw) return;
        needsRedraw = false;
        Redraw();
    }

    /// <summary>Listeyi boşaltır.</summary>
    public void Clear()
    {
        tasks.Clear();
        tasksById.Clear();
        needsRedraw = true;
    }

    /// <summary>
    /// Listenin sonuna bekleyen bir görev ekler. Aynı kimlik varsa etiketi
    /// güncellenir, satır çoğaltılmaz.
    /// </summary>
    /// <param name="id">Kısa kimlik, örn. "WP-03".</param>
    /// <param name="label">Açıklama metni.</param>
    public void AddTask(string id, string label)
    {
        if (string.IsNullOrEmpty(id)) return;

        if (tasksById.TryGetValue(id, out Task existing))
        {
            existing.Label = label;
            needsRedraw = true;
            return;
        }

        var task = new Task { Id = id, Label = label, State = HarmonyTaskState.Pending };
        tasks.Add(task);
        tasksById[id] = task;
        needsRedraw = true;
    }

    /// <summary>
    /// Bir görevin durumunu değiştirir.
    /// </summary>
    /// <param name="id">Görev kimliği.</param>
    /// <param name="state">Yeni durum.</param>
    public void SetState(string id, HarmonyTaskState state)
    {
        if (string.IsNullOrEmpty(id)) return;
        if (!tasksById.TryGetValue(id, out Task task)) return;
        if (task.State == state) return;

        task.State = state;
        needsRedraw = true;
    }

    /// <summary>
    /// Hâlâ bekleyen ya da yürütülen tüm görevleri verilen duruma alır.
    /// Görev iptalinde (STOP) kullanılır.
    /// </summary>
    /// <param name="state">Uygulanacak durum.</param>
    public void SetAllUnfinished(HarmonyTaskState state)
    {
        for (int i = 0; i < tasks.Count; i++)
            if (tasks[i].State != HarmonyTaskState.Completed) tasks[i].State = state;

        needsRedraw = true;
    }

    /// <summary>
    /// Listeyi yeniden çizer: satırlar, ilerleme sayacı ve çubuk.
    /// </summary>
    private void Redraw()
    {
        if (listText != null)
            listText.text = tasks.Count == 0 ? emptyMessage : BuildRows();

        int done = CompletedCount;
        int total = tasks.Count;

        if (progressText != null)
        {
            progressText.text = total == 0
                ? "<color=#64748b>—</color>"
                : $"<b>{done}</b> <color=#64748b>/ {total} completed</color>";
        }

        if (progressFill != null && progressFullWidth > 0f)
        {
            float ratio = total > 0 ? done / (float)total : 0f;
            var size = progressFill.sizeDelta;
            progressFill.sizeDelta = new Vector2(progressFullWidth * ratio, size.y);
        }
    }

    /// <summary>Görev satırlarını tek metne birleştirir.</summary>
    private string BuildRows()
    {
        builder.Length = 0;

        for (int i = 0; i < tasks.Count; i++)
        {
            Task t = tasks[i];

            // Tamamlanan satırlar sönükleşsin ki gözün sırada olana gitsin.
            bool dim = t.State == HarmonyTaskState.Completed;
            string idColor = dim ? "#475569" : "#e2e8f0";
            string labelColor = dim ? "#475569" : "#94a3b8";

            builder.Append(ColId).Append("<color=").Append(idColor).Append("><b>").Append(t.Id).Append("</b></color>")
                   .Append(ColLabel).Append("<color=").Append(labelColor).Append('>').Append(t.Label).Append("</color>")
                   .Append(ColState).Append(StateToRichText(t.State));

            if (i < tasks.Count - 1) builder.Append('\n');
        }

        return builder.ToString();
    }

    /// <summary>
    /// Durumun renkli etiket karşılığı. Renkler kusur tablosuyla aynı paletten.
    /// </summary>
    /// <param name="state">Görev durumu.</param>
    private static string StateToRichText(HarmonyTaskState state)
    {
        switch (state)
        {
            case HarmonyTaskState.InProgress:
                return "<color=#f59e0b><b>IN PROGRESS</b></color>";
            case HarmonyTaskState.Completed:
                return "<color=#10b981><b>COMPLETED</b></color>";
            case HarmonyTaskState.Aborted:
                return "<color=#ef4444><b>ABORTED</b></color>";
            default:
                return "<color=#64748b>PENDING</color>";
        }
    }
}
