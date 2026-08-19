using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Arayüzü ROS ve gözlük olmadan denemek için test paneli.
///
/// İki tür deneme sunar:
///   1. Gerçek komut butonlarını tetikleme — butonun onClick zinciri aynen
///      çalışır, yani ExecCommandViaSocket üzerinden /harmony/cmd_input'a
///      mesaj gider (rosbridge bağlıysa).
///   2. Gelen veriyi taklit etme — kusur, görev durumu ve telemetri
///      doğrudan ilgili bileşenlere enjekte edilir; ROS'a hiç ihtiyaç yoktur.
///
/// Metotlar Inspector'daki butonlardan (bkz. HarmonyUiTesterEditor) veya
/// bileşenin bağlam menüsünden çağrılır. Yalnızca Play modunda anlamlıdır.
/// </summary>
public class HarmonyUiTester : MonoBehaviour
{
    /// <summary>Senaryodaki sabit kusurlar (harmony_defects.py ile aynı).</summary>
    private static readonly string[,] Defects =
    {
        { "DEF-01", "Protrusion", "-1.100", "0.800", "1.520" },
        { "DEF-02", "Protrusion", "-1.100", "0.300", "1.530" },
        { "DEF-03", "Dent",       "-0.918", "0.274", "1.040" },
        { "DEF-04", "Dent",       "-0.892", "1.680", "1.000" }
    };

    private static readonly string[] ChartJoints =
    {
        "shoulder_pan_joint", "wrist_3_joint", "elbow_joint", "wrist_2_joint",
        "base_to_robot_mount", "wrist_1_joint", "shoulder_lift_joint"
    };

    [Header("Gerçek Komut Butonları")]
    [Tooltip("Boş bırakılırsa sahnede ada göre aranır.")]
    [SerializeField] private Button startButton;
    [SerializeField] private Button confirmButton;
    [SerializeField] private Button reinspectButton;
    [SerializeField] private Button stopButton;

    [Header("Veri Hedefleri")]
    [Tooltip("Boş bırakılırsa sahnede aranır.")]
    [SerializeField] private HarmonyDefectSubscriber defectSubscriber;
    [SerializeField] private HarmonyMissionController missionController;
    [SerializeField] private HarmonyTelemetrySubscriber telemetry;

    [Header("Sahte Telemetri")]
    [Tooltip("Açıkken eklem ve kuvvet verisi üretilir; grafikler canlanır.")]
    [SerializeField] private bool fakeTelemetryRunning;

    [Tooltip("Sahte kuvvetin salınım merkezi [N].")]
    [SerializeField] private float fakeForceCenter = 0.45f;

    [Tooltip("Sahte kuvvetin salınım genliği [N].")]
    [SerializeField] private float fakeForceAmplitude = 0.3f;

    // Sıradaki temizlenecek kusur.
    private int cleanCursor;

    private void Awake() => ResolveReferences();

    /// <summary>
    /// Atanmamış referansları sahneden bulur.
    /// </summary>
    public void ResolveReferences()
    {
        if (defectSubscriber == null) defectSubscriber = FindObjectOfType<HarmonyDefectSubscriber>();
        if (missionController == null) missionController = FindObjectOfType<HarmonyMissionController>();
        if (telemetry == null) telemetry = FindObjectOfType<HarmonyTelemetrySubscriber>();

        if (startButton == null) startButton = FindButton("BtnStart");
        if (confirmButton == null) confirmButton = FindButton("BtnConfirm");
        if (reinspectButton == null) reinspectButton = FindButton("BtnReinspect");
        if (stopButton == null) stopButton = FindButton("BtnStop");
    }

    private static Button FindButton(string name)
    {
        foreach (var b in FindObjectsOfType<Button>(true))
            if (b.gameObject.name == name) return b;
        return null;
    }

    // =====================================================================
    // 1) Gerçek buton tetikleme — onClick zinciri aynen çalışır
    // =====================================================================

    /// <summary>START butonuna basılmış gibi davranır.</summary>
    [ContextMenu("1) Buton: START")]
    public void ClickStart() => Click(startButton, "START");

    /// <summary>CONFIRM butonuna basılmış gibi davranır.</summary>
    [ContextMenu("1) Buton: CONFIRM")]
    public void ClickConfirm() => Click(confirmButton, "CONFIRM");

    /// <summary>REINSPECT butonuna basılmış gibi davranır.</summary>
    [ContextMenu("1) Buton: REINSPECT")]
    public void ClickReinspect() => Click(reinspectButton, "REINSPECT");

    /// <summary>STOP butonuna basılmış gibi davranır.</summary>
    [ContextMenu("1) Buton: STOP")]
    public void ClickStop() => Click(stopButton, "STOP");

    private void Click(Button b, string label)
    {
        if (b == null)
        {
            Debug.LogWarning($"HarmonyUiTester: {label} butonu bulunamadı.");
            return;
        }

        b.onClick.Invoke();
        Debug.Log($"HarmonyUiTester: {label} butonu tetiklendi ({b.onClick.GetPersistentEventCount()} kalıcı çağrı).");
    }

    // =====================================================================
    // 2) Görev durumu taklidi
    // =====================================================================

    /// <summary>Görevi boşta durumuna alır.</summary>
    [ContextMenu("2) Durum: IDLE")]
    public void StateIdle() => PushState("IDLE", "");

    /// <summary>Tarama durumuna alır.</summary>
    [ContextMenu("2) Durum: SR_MODE (tarama)")]
    public void StateSensing() => PushState("SR_MODE", "Kapi yuzeyi taraniyor");

    /// <summary>Onay bekleme durumuna alır — onay bandı burada görünür.</summary>
    [ContextMenu("2) Durum: WAITING (onay bandi)")]
    public void StateWaiting() => PushState("WAITING", "Kusurlar bulundu, onay bekleniyor");

    /// <summary>Giderim durumuna alır.</summary>
    [ContextMenu("2) Durum: CR_MODE (giderim)")]
    public void StateCleaning() => PushState("CR_MODE", "Kusurlar gideriliyor");

    private void PushState(string state, string note)
    {
        if (missionController == null) { Debug.LogWarning("HarmonyUiTester: MissionController yok."); return; }
        missionController.InjectStatusJson("{\"state\":\"" + state + "\",\"note\":\"" + note + "\"}");
        Debug.Log($"HarmonyUiTester: durum → {state}");
    }

    // =====================================================================
    // 3) Kusur taklidi
    // =====================================================================

    /// <summary>Senaryodaki dört kusuru tabloya ekler.</summary>
    [ContextMenu("3) Kusurlar: 4 kusur gonder")]
    public void PushAllDefects()
    {
        if (defectSubscriber == null) { Debug.LogWarning("HarmonyUiTester: DefectSubscriber yok."); return; }

        for (int i = 0; i < Defects.GetLength(0); i++)
        {
            string json =
                "{\"defect_id\":\"" + Defects[i, 0] + "\"," +
                "\"defect_type\":\"" + Defects[i, 1] + "\"," +
                "\"frame_id\":\"world\"," +
                "\"position\":{\"x\":" + Defects[i, 2] + ",\"y\":" + Defects[i, 3] + ",\"z\":" + Defects[i, 4] + "}," +
                "\"approach\":{\"x\":0.0,\"y\":0.0,\"z\":0.0}," +
                "\"status\":\"DETECTED\"}";
            defectSubscriber.InjectDefectJson(json);
        }

        cleanCursor = 0;
        Debug.Log("HarmonyUiTester: 4 kusur gönderildi.");
    }

    /// <summary>Sıradaki kusuru CLEANING durumuna geçirir.</summary>
    [ContextMenu("3) Kusurlar: sirdakini CLEANING yap")]
    public void MarkNextCleaning() => PushStatus(cleanCursor, "CLEANING", false);

    /// <summary>Sıradaki kusuru CLEANED yapar ve imleci ilerletir.</summary>
    [ContextMenu("3) Kusurlar: sirdakini CLEANED yap")]
    public void MarkNextCleaned() => PushStatus(cleanCursor, "CLEANED", true);

    /// <summary>Tüm kusurları giderilmiş olarak işaretler.</summary>
    [ContextMenu("3) Kusurlar: hepsini CLEANED yap")]
    public void MarkAllCleaned()
    {
        for (int i = 0; i < Defects.GetLength(0); i++) PushStatus(i, "CLEANED", false);
        cleanCursor = Defects.GetLength(0);
    }

    /// <summary>Kusur tablosunu boşaltır.</summary>
    [ContextMenu("3) Kusurlar: tabloyu temizle")]
    public void ClearDefects()
    {
        if (defectSubscriber == null) return;
        defectSubscriber.Clear();
        cleanCursor = 0;
        Debug.Log("HarmonyUiTester: kusur tablosu temizlendi.");
    }

    private void PushStatus(int index, string status, bool advance)
    {
        if (defectSubscriber == null) return;
        if (index < 0 || index >= Defects.GetLength(0))
        {
            Debug.Log("HarmonyUiTester: sırada kusur kalmadı.");
            return;
        }

        defectSubscriber.InjectStatusJson(
            "{\"defect_id\":\"" + Defects[index, 0] + "\",\"status\":\"" + status + "\"}");
        Debug.Log($"HarmonyUiTester: {Defects[index, 0]} → {status}");

        if (advance) cleanCursor++;
    }

    // =====================================================================
    // 4) Sahte telemetri
    // =====================================================================

    /// <summary>Sahte telemetri üretimini açar/kapatır.</summary>
    [ContextMenu("4) Telemetri: ac/kapa")]
    public void ToggleFakeTelemetry()
    {
        fakeTelemetryRunning = !fakeTelemetryRunning;
        Debug.Log("HarmonyUiTester: sahte telemetri " + (fakeTelemetryRunning ? "açık" : "kapalı"));
    }

    /// <summary>Sahte telemetri açık mı?</summary>
    public bool FakeTelemetryRunning => fakeTelemetryRunning;

    private void Update()
    {
        if (!fakeTelemetryRunning || telemetry == null) return;

        float t = Time.time;

        // Her eklem farklı fazda salınsın ki grafikte ayrı eğriler görünsün.
        for (int i = 0; i < ChartJoints.Length; i++)
        {
            float phase = i * 0.7f;
            float pos = Mathf.Sin(t * 0.6f + phase) * (0.8f - i * 0.08f);
            float vel = Mathf.Cos(t * 0.6f + phase) * 0.4f;
            telemetry.InjectJoint(ChartJoints[i], pos, vel);
        }

        telemetry.InjectForce(fakeForceCenter + Mathf.Sin(t * 1.3f) * fakeForceAmplitude);
    }

    // =====================================================================
    // 5) Tam senaryo
    // =====================================================================

    /// <summary>
    /// Tüm akışı sırayla oynatır: tarama → kusurlar → onay → giderim.
    /// Panellerin her halini tek tıklamada görmek için.
    /// </summary>
    [ContextMenu("5) Tam senaryoyu oynat")]
    public void PlayFullScenario()
    {
        StopAllCoroutines();
        StartCoroutine(FullScenarioRoutine());
    }

    private System.Collections.IEnumerator FullScenarioRoutine()
    {
        ClearDefects();
        fakeTelemetryRunning = true;

        StateSensing();
        yield return new WaitForSeconds(2f);

        PushAllDefects();
        yield return new WaitForSeconds(1.5f);

        StateWaiting();
        yield return new WaitForSeconds(2.5f);

        StateCleaning();

        for (int i = 0; i < Defects.GetLength(0); i++)
        {
            PushStatus(i, "CLEANING", false);
            yield return new WaitForSeconds(1.2f);
            PushStatus(i, "CLEANED", false);
            yield return new WaitForSeconds(0.6f);
        }

        cleanCursor = Defects.GetLength(0);
        StateIdle();
        Debug.Log("HarmonyUiTester: senaryo tamamlandı.");
    }
}
