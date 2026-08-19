using System.Text;
using TMPro;
using UnityEngine;

/// <summary>
/// Bir kusur işaretçisinin baloncuğu: küreden çıkan çekme çizgisi ve çizginin
/// ucundaki özellik paneli.
///
/// Bileşen kusur küresinin altına çocuk olarak kurulur. Küre
/// <see cref="HarmonyDefectSurfaceMapper"/> tarafından işaretçi çapı kadar
/// ölçeklendiği için, baloncuk kökü bu ölçeği geri alır ve içerideki her şey
/// metre cinsinden çalışır. Yani <see cref="Build"/> parametreleri doğrudan
/// metredir.
///
/// Panel boyutu içeriğe göre büyür: verilen boyut alt sınırdır, yazı sığmıyorsa
/// zemin ve kenarlık yazının ölçüsüne genişletilir. Böylece metin hiçbir
/// durumda panelin dışına taşmaz.
///
/// Tema rengi (küre, çizgi, panel kenarı) dışarıdan <see cref="SetTheme"/> ile
/// verilir; panel zemini ve yazı sabit kalır ki metin her renkte okunur olsun.
/// </summary>
public class HarmonyDefectCallout : MonoBehaviour
{
    /// <summary>Panel zemini — koyu ve yarı saydam, yazı her temada okunur kalsın.</summary>
    private static readonly Color PanelBackground = new Color32(0x0f, 0x17, 0x2a, 210);

    /// <summary>Yazı ile panel kenarı arasındaki boşluk, satır yüksekliğinin oranı.</summary>
    private const float PaddingRatio = 0.5f;

    /// <summary>Kenarlığın zeminle z-fighting yapmaması için öne alınma payı [m].</summary>
    private const float BorderZ = -0.001f;

    private Transform calloutRoot;
    private LineRenderer leaderLine;
    private Transform panelRoot;
    private Transform panelBackgroundTransform;
    private Renderer panelBackground;
    private LineRenderer panelBorder;
    private TextMeshPro panelText;

    private Vector3 panelOffset;
    private Vector2 minPanelSize;

    /// <summary>İstenen satır yüksekliği [m]. TMP'nin fontSize birimi buna eşit değildir.</summary>
    private float lineHeight;

    private float lineWidth;
    private bool billboard;
    private Camera billboardCamera;

    /// <summary>Panelin son hesaplanan gerçek boyutu [m].</summary>
    private Vector2 currentPanelSize;

    /// <summary>
    /// Baloncuğu kurar. İşaretçi küresi üzerinde bir kez çağrılır.
    /// </summary>
    /// <param name="scaleCompensation">
    /// Küre ölçeğini geri almak için çarpan; genelde 1 / markerSize.
    /// </param>
    /// <param name="offset">Panel merkezinin küreye göre konumu [m].</param>
    /// <param name="minSize">Panelin en küçük genişlik ve yüksekliği [m].</param>
    /// <param name="textLineHeight">Bir yazı satırının yüksekliği [m].</param>
    /// <param name="width">Çizgi kalınlığı [m].</param>
    /// <param name="faceCamera">Panel kameraya dönsün mü.</param>
    public void Build(
        float scaleCompensation,
        Vector3 offset,
        Vector2 minSize,
        float textLineHeight,
        float width,
        bool faceCamera)
    {
        panelOffset = offset;
        minPanelSize = minSize;
        lineHeight = Mathf.Max(textLineHeight, 1e-4f);
        lineWidth = width;
        billboard = faceCamera;
        currentPanelSize = minSize;

        // Kökün ölçeği kürenin ölçeğini geri alır; içeride 1 birim = 1 metre.
        var rootGo = new GameObject("Callout");
        calloutRoot = rootGo.transform;
        calloutRoot.SetParent(transform, false);
        calloutRoot.localPosition = Vector3.zero;
        calloutRoot.localRotation = Quaternion.identity;
        calloutRoot.localScale = Vector3.one * scaleCompensation;

        BuildLeaderLine();
        BuildPanel();
        ApplyPanelSize(currentPanelSize);
    }

    /// <summary>Küreden panele giden çekme çizgisini kurar.</summary>
    private void BuildLeaderLine()
    {
        var go = new GameObject("LeaderLine");
        go.transform.SetParent(calloutRoot, false);

        leaderLine = go.AddComponent<LineRenderer>();
        leaderLine.useWorldSpace = false;
        leaderLine.positionCount = 2;
        leaderLine.widthMultiplier = lineWidth;
        leaderLine.numCornerVertices = 2;
        leaderLine.numCapVertices = 2;
        leaderLine.SetPosition(0, Vector3.zero);
        leaderLine.SetPosition(1, panelOffset);
        leaderLine.material = CreateUnlitMaterial();
    }

    /// <summary>Özellik panelini (zemin, kenarlık, yazı) kurar.</summary>
    private void BuildPanel()
    {
        var go = new GameObject("Panel");
        panelRoot = go.transform;
        panelRoot.SetParent(calloutRoot, false);
        panelRoot.localPosition = panelOffset;

        // --- Zemin ---
        var bgGo = GameObject.CreatePrimitive(PrimitiveType.Quad);
        bgGo.name = "Background";
        panelBackgroundTransform = bgGo.transform;
        panelBackgroundTransform.SetParent(panelRoot, false);

        // Çarpışma gerekmez; XR ray'lerini engellemesin.
        var col = bgGo.GetComponent<Collider>();
        if (col != null) Destroy(col);

        panelBackground = bgGo.GetComponent<Renderer>();
        panelBackground.material = CreateUnlitMaterial();
        panelBackground.material.color = PanelBackground;

        // --- Kenarlık ---
        var borderGo = new GameObject("Border");
        borderGo.transform.SetParent(panelRoot, false);

        panelBorder = borderGo.AddComponent<LineRenderer>();
        panelBorder.useWorldSpace = false;
        panelBorder.loop = true;
        panelBorder.positionCount = 4;
        panelBorder.widthMultiplier = lineWidth;
        panelBorder.numCornerVertices = 2;
        panelBorder.numCapVertices = 2;
        panelBorder.material = CreateUnlitMaterial();

        // --- Yazı ---
        var textGo = new GameObject("Text");
        textGo.transform.SetParent(panelRoot, false);
        textGo.transform.localPosition = new Vector3(0f, 0f, BorderZ * 2f);

        panelText = textGo.AddComponent<TextMeshPro>();
        panelText.alignment = TextAlignmentOptions.Left;
        panelText.color = Color.white;
        panelText.enableWordWrapping = false;
    }

    /// <summary>
    /// TMP'nin fontSize birimini istenen satır yüksekliğine [m] kalibre eder.
    /// Oran font varlığına göre değiştiği (LiberationSans SDF'te ~1/9) ve
    /// sabit varsaymak yazıyı panele göre okunmaz hale getirdiği için, referans
    /// bir boyutta ölçüp ölçekliyoruz.
    /// </summary>
    private void CalibrateFontSize()
    {
        if (panelText == null || string.IsNullOrEmpty(panelText.text)) return;

        const float probeSize = 1f;
        panelText.fontSize = probeSize;
        panelText.ForceMeshUpdate();

        int lines = Mathf.Max(1, panelText.textInfo.lineCount);
        Vector2 probe = panelText.GetPreferredValues(
            panelText.text, Mathf.Infinity, Mathf.Infinity);

        float heightPerLine = probe.y / lines;
        if (heightPerLine <= 1e-6f) return;

        panelText.fontSize = probeSize * (lineHeight / heightPerLine);
        panelText.ForceMeshUpdate();
    }

    /// <summary>
    /// Işıktan bağımsız, her koşulda okunur bir malzeme üretir.
    /// AR'da sahne aydınlatması güvenilir olmadığı için unlit tercih edilir.
    /// </summary>
    private static Material CreateUnlitMaterial()
    {
        Shader shader = Shader.Find("Sprites/Default");
        if (shader == null) shader = Shader.Find("Unlit/Color");
        if (shader == null) shader = Shader.Find("Standard");
        return new Material(shader);
    }

    /// <summary>
    /// Panel içeriğini yazar ve paneli yazıya sığacak şekilde büyütür.
    /// </summary>
    /// <param name="defect">Gösterilecek kusur kaydı.</param>
    public void SetContent(HarmonyDefect defect)
    {
        if (panelText == null || defect == null) return;

        var sb = new StringBuilder();
        sb.Append("<b>").Append(defect.Id).Append("</b>\n");
        sb.Append(defect.DefectType).Append('\n');
        sb.Append(StatusText(defect.Status)).Append('\n');
        sb.AppendFormat(
            "x: {0:0.00}  y: {1:0.00}  z: {2:0.00}",
            defect.RosPosition.x, defect.RosPosition.y, defect.RosPosition.z);

        panelText.text = sb.ToString();
        CalibrateFontSize();
        FitPanelToText();
    }

    /// <summary>
    /// Zemini, kenarlığı ve yazı alanını metnin gerçek ölçüsüne göre yeniden
    /// boyutlandırır. Verilen en küçük boyutun altına inilmez.
    /// </summary>
    private void FitPanelToText()
    {
        if (panelText == null) return;

        float padding = lineHeight * PaddingRatio;

        // Yazı alanını serbest bırakıp gerçek tercih edilen ölçüyü ölç.
        Vector2 preferred = panelText.GetPreferredValues(
            panelText.text, Mathf.Infinity, Mathf.Infinity);

        var size = new Vector2(
            Mathf.Max(minPanelSize.x, preferred.x + padding * 2f),
            Mathf.Max(minPanelSize.y, preferred.y + padding * 2f));

        ApplyPanelSize(size);
    }

    /// <summary>
    /// Hesaplanan boyutu zemine, kenarlığa, yazı alanına ve çekme çizgisinin
    /// bitiş noktasına uygular.
    /// </summary>
    /// <param name="size">Panel genişlik ve yüksekliği [m].</param>
    private void ApplyPanelSize(Vector2 size)
    {
        currentPanelSize = size;

        float halfWidth = size.x * 0.5f;
        float halfHeight = size.y * 0.5f;

        if (panelBackgroundTransform != null)
            panelBackgroundTransform.localScale = new Vector3(size.x, size.y, 1f);

        if (panelBorder != null)
        {
            panelBorder.SetPosition(0, new Vector3(-halfWidth, -halfHeight, BorderZ));
            panelBorder.SetPosition(1, new Vector3(halfWidth, -halfHeight, BorderZ));
            panelBorder.SetPosition(2, new Vector3(halfWidth, halfHeight, BorderZ));
            panelBorder.SetPosition(3, new Vector3(-halfWidth, halfHeight, BorderZ));
        }

        if (panelText != null)
        {
            float padding = lineHeight * PaddingRatio;
            panelText.rectTransform.sizeDelta =
                new Vector2(size.x - padding * 2f, size.y - padding * 2f);
        }

        // Çizgi panelin merkezine değil kenarına dayansın; aksi halde ucu
        // zeminin altında kalır ve baloncuk panele saplanmış görünür.
        if (leaderLine != null && panelOffset != Vector3.zero)
        {
            float stop = Mathf.Min(halfWidth, halfHeight);
            Vector3 endPoint = panelOffset - panelOffset.normalized * stop;

            // Panel küreyi yutacak kadar yakınsa çizgiyi tümden gizle.
            if (Vector3.Dot(endPoint, panelOffset) <= 0f)
            {
                leaderLine.enabled = false;
            }
            else
            {
                leaderLine.enabled = true;
                leaderLine.SetPosition(1, endPoint);
            }
        }
    }

    /// <summary>Durum enum'unu panelde gösterilecek metne çevirir.</summary>
    /// <param name="status">Kusur durumu.</param>
    private static string StatusText(DefectStatus status)
    {
        switch (status)
        {
            case DefectStatus.Detected: return "DETECTED";
            case DefectStatus.Cleaning: return "CLEANING";
            case DefectStatus.Cleaned: return "CLEANED";
            case DefectStatus.Failed: return "FAILED";
            default: return "UNKNOWN";
        }
    }

    /// <summary>
    /// Tema rengini çekme çizgisine ve panel kenarlığına uygular.
    /// Küreyi çağıran taraf boyar; panel zemini ve yazı bilerek dışarıda kalır.
    /// </summary>
    /// <param name="color">Kusurun tür/durum rengi.</param>
    public void SetTheme(Color color)
    {
        if (leaderLine != null)
        {
            leaderLine.startColor = color;
            leaderLine.endColor = color;
            if (leaderLine.material != null) leaderLine.material.color = color;
        }

        if (panelBorder != null)
        {
            panelBorder.startColor = color;
            panelBorder.endColor = color;
            if (panelBorder.material != null) panelBorder.material.color = color;
        }
    }

    /// <summary>
    /// Verilen renderer baloncuğa mı ait? Kürenin renklendirmesi baloncuğun
    /// zeminini ve yazısını ezmesin diye kullanılır.
    /// </summary>
    /// <param name="renderer">Sınanacak renderer.</param>
    public bool Owns(Renderer renderer)
    {
        if (renderer == null || calloutRoot == null) return false;
        return renderer.transform.IsChildOf(calloutRoot);
    }

    /// <summary>Panelin son hesaplanan boyutu [m]; sınama için.</summary>
    public Vector2 PanelSize => currentPanelSize;

    /// <summary>Panelin küreye göre güncel konumu [m].</summary>
    public Vector3 Offset => panelOffset;

    /// <summary>
    /// Panelin küreye göre konumunu değiştirir. Yerleşim yöneticisi kusurlar
    /// birbirine yakın olduğunda baloncukları farklı yönlere dağıtmak için
    /// kullanır. Çekme çizgisi ve panel birlikte taşınır.
    /// </summary>
    /// <param name="offset">Yeni panel konumu [m].</param>
    public void SetOffset(Vector3 offset)
    {
        if (panelOffset == offset) return;

        panelOffset = offset;
        if (panelRoot != null) panelRoot.localPosition = offset;

        // Çizginin bitiş noktası ve panel kenarı birlikte yeniden hesaplanır.
        ApplyPanelSize(currentPanelSize);
    }

    /// <summary>Yazının kapladığı gerçek alan [m]; sınama için.</summary>
    public Vector2 TextSize =>
        panelText == null
            ? Vector2.zero
            : panelText.GetPreferredValues(panelText.text, Mathf.Infinity, Mathf.Infinity);

    private void LateUpdate()
    {
        if (!billboard || panelRoot == null) return;

        if (billboardCamera == null) billboardCamera = Camera.main;
        if (billboardCamera == null) return;

        // Panel kameraya dönsün; çekme çizgisi sabit kaldığı için baloncuk
        // küreye bağlı görünmeye devam eder.
        panelRoot.rotation = Quaternion.LookRotation(
            panelRoot.position - billboardCamera.transform.position,
            billboardCamera.transform.up);
    }
}
