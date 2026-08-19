using System.Collections.Generic;
using System.Text;
using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.XR.Hands;

#if UNITY_ANDROID
using UnityEngine.Android;
#endif

/// <summary>
/// El takibi zincirinin her halkasını cihazda okunabilir biçimde gösterir.
///
/// El ışını çalışmadığında sorun şu halkalardan birindedir ve dışarıdan hepsi
/// aynı görünür: izin verilmemiştir, alt sistem çalışmıyordur, eklemler
/// gelmiyordur, ya da işaretleme pozu boştur. Bu panel hangisinin koptuğunu
/// tek bakışta söyler; logcat'e bakmadan.
///
/// Yalnızca okur, hiçbir şeyi değiştirmez.
/// </summary>
public class HarmonyXrDiagnostics : MonoBehaviour
{
    private const string MlHandTrackingPermission = "com.magicleap.permission.HAND_TRACKING";

    /// <summary>XR_EXT_hand_interaction ve benzeri yerel işaretleme cihazlarının düzen adları.</summary>
    private static readonly string[] NativeAimLayouts = { "HandInteraction", "HandInteractionPoses", "MetaAimHand" };

    [Header("Çıktı")]
    [Tooltip("Raporun yazılacağı metin alanı.")]
    [SerializeField] private TMP_Text output;

    [Tooltip("Rapor tazeleme aralığı [s].")]
    [SerializeField] private float refreshInterval = 0.5f;

    [Header("İzlenecek Nesneler")]
    [Tooltip("Sol elin Aim Pose transform'u.")]
    [SerializeField] private Transform leftAim;

    [Tooltip("Sağ elin Aim Pose transform'u.")]
    [SerializeField] private Transform rightAim;

    [Tooltip("Konumları göreli okumak için XR Origin.")]
    [SerializeField] private Transform trackingOrigin;

    private static readonly List<XRHandSubsystem> SubsystemBuffer = new List<XRHandSubsystem>();

    private readonly StringBuilder builder = new StringBuilder(1024);
    private float nextRefresh;

    private void Update()
    {
        if (output == null) return;
        if (Time.unscaledTime < nextRefresh) return;
        nextRefresh = Time.unscaledTime + Mathf.Max(refreshInterval, 0.1f);

        output.text = BuildReport();
    }

    /// <summary>Zincirin tüm halkalarını tek metinde toplar.</summary>
    private string BuildReport()
    {
        builder.Length = 0;

        // En kritik bölüm en üstte: ışının çıktığı nesne gerçekten hareket
        // ediyor mu? Cihaz listesi uzun olduğu için altta kalmalı, yoksa
        // panelden taşıp bu satırları görünmez yapıyor.
        AppendAimTransforms();
        AppendHandInteractionRaw();
        AppendRuntime();
        AppendPermission();
        AppendSubsystem();
        AppendDevices();

        return builder.ToString();
    }

    /// <summary>
    /// HandInteraction cihazından ham değerleri okur.
    ///
    /// Cihaz düzeyindeki <c>trackingState</c>, profil tanımına göre grip
    /// pozunun durumunu yansıtır (HandInteractionProfile.cs:120). Pointer
    /// pozunun konumu doluyken bu bayrakta Position biti gelmiyorsa
    /// TrackedPoseDriver konumu eler — ışının yerinde çakılı kalmasının
    /// sebebi budur. Bu satırlar o durumu doğrudan gösterir.
    /// </summary>
    private void AppendHandInteractionRaw()
    {
        builder.Append("<color=#64748b>--- HandInteraction ham degerler ---</color>\n");

        AppendRawHand("Sol", "LeftHand");
        AppendRawHand("Sag", "RightHand");
    }

    /// <summary>
    /// Bir elin HandInteraction cihazındaki tracking state, pointer konumu ve
    /// tetik değerini yazar.
    /// </summary>
    /// <param name="label">Satır etiketi.</param>
    /// <param name="usage">Aranacak el kullanımı, "LeftHand" veya "RightHand".</param>
    private void AppendRawHand(string label, string usage)
    {
        InputDevice device = null;

        foreach (InputDevice d in InputSystem.devices)
        {
            if (!InputSystem.IsFirstLayoutBasedOnSecond(d.layout, "HandInteraction")) continue;

            bool match = false;
            foreach (var u in d.usages) if (u == usage) match = true;
            if (!match) continue;

            device = d;
            break;
        }

        if (device == null)
        {
            Row(label, "HandInteraction cihazi yok", false);
            return;
        }

        var ts = device.TryGetChildControl<UnityEngine.InputSystem.Controls.IntegerControl>("trackingState");
        var pos = device.TryGetChildControl<UnityEngine.InputSystem.Controls.Vector3Control>("pointer/position");
        var act = device.TryGetChildControl<UnityEngine.InputSystem.Controls.AxisControl>("pointerActivateValue");

        int stateBits = ts != null ? ts.ReadValue() : -1;
        bool hasPos = (stateBits & 1) != 0;   // InputTrackingState.Position
        bool hasRot = (stateBits & 2) != 0;   // InputTrackingState.Rotation

        Row(label + " trackingState", stateBits + "  (Position=" + hasPos + " Rotation=" + hasRot + ")", hasPos);

        Vector3 p = pos != null ? pos.ReadValue() : Vector3.zero;
        Row(label + " pointer/position", pos == null ? "kontrol yok" : Fmt(p), pos != null && p.sqrMagnitude > 1e-6f);

        Row(label + " pointerActivate", act == null ? "kontrol yok" : act.ReadValue().ToString("F2"), act != null);
    }

    private void AppendRuntime()
    {
        string runtime;
        try
        {
            runtime = string.IsNullOrEmpty(UnityEngine.XR.OpenXR.OpenXRRuntime.name)
                ? "(bos)"
                : UnityEngine.XR.OpenXR.OpenXRRuntime.name;
        }
        catch (System.Exception e)
        {
            runtime = "hata: " + e.Message;
        }

        Row("OpenXR runtime", runtime, !runtime.StartsWith("hata") && runtime != "(bos)");
    }

    private void AppendPermission()
    {
#if UNITY_ANDROID
        bool granted = Application.isEditor || Permission.HasUserAuthorizedPermission(MlHandTrackingPermission);
        Row("HAND_TRACKING izni", granted ? "VERILDI" : "YOK", granted);
#else
        Row("HAND_TRACKING izni", "Android disi", false);
#endif
    }

    private void AppendSubsystem()
    {
        SubsystemManager.GetSubsystems(SubsystemBuffer);

        XRHandSubsystem running = null;
        for (int i = 0; i < SubsystemBuffer.Count; i++)
            if (SubsystemBuffer[i].running) running = SubsystemBuffer[i];

        Row("El alt sistemi", running != null ? "CALISIYOR" : (SubsystemBuffer.Count > 0 ? "VAR ama durmus" : "YOK"),
            running != null);

        if (running == null) return;

        AppendHand("Sol el", running.leftHand);
        AppendHand("Sag el", running.rightHand);
    }

    /// <summary>Bir elin izlenme durumunu ve örnek bir eklem konumunu yazar.</summary>
    /// <param name="label">Satır etiketi.</param>
    /// <param name="hand">Okunacak el.</param>
    private void AppendHand(string label, XRHand hand)
    {
        if (!hand.isTracked)
        {
            Row(label, "izlenmiyor", false);
            return;
        }

        Pose pose;
        if (!hand.GetJoint(XRHandJointID.IndexProximal).TryGetPose(out pose))
        {
            Row(label, "izleniyor ama eklem pozu YOK", false);
            return;
        }

        if (trackingOrigin != null)
            pose = pose.GetTransformedBy(new Pose(trackingOrigin.position, trackingOrigin.rotation));

        Row(label, "izleniyor  boğum=" + Fmt(pose.position), true);
    }

    private void AppendDevices()
    {
        bool nativeAim = false;
        int count = 0;

        builder.Append("<color=#64748b>--- Input System cihazlari ---</color>\n");

        foreach (InputDevice device in InputSystem.devices)
        {
            count++;

            bool isAim = false;
            for (int i = 0; i < NativeAimLayouts.Length; i++)
            {
                if (!InputSystem.IsFirstLayoutBasedOnSecond(device.layout, NativeAimLayouts[i])) continue;
                isAim = true;
                nativeAim = true;
                break;
            }

            builder.Append(isAim ? "  <color=#2dd4bf>" : "  <color=#94a3b8>")
                   .Append(device.layout).Append("</color>  <color=#475569>")
                   .Append(string.Join(",", UsageNames(device))).Append("</color>\n");
        }

        if (count == 0) builder.Append("  <color=#ef4444>(hic cihaz yok)</color>\n");

        Row("Yerel isaret cihazi", nativeAim ? "VAR" : "YOK — poz eklemlerden uretilecek", nativeAim);
    }

    private static string[] UsageNames(InputDevice device)
    {
        var list = new List<string>();
        foreach (var u in device.usages) list.Add(u.ToString());
        return list.ToArray();
    }

    private void AppendAimTransforms()
    {
        builder.Append("<color=#64748b>--- Aim Pose nesneleri ---</color>\n");
        AppendAim("Sol", leftAim);
        AppendAim("Sag", rightAim);
    }

    /// <summary>
    /// Aim Pose nesnesinin konumunu ve XR Origin'e uzaklığını yazar.
    /// Sıfıra yakın uzaklık, pozun hiç yazılmadığı anlamına gelir — ışının
    /// kafadan çıkması tam olarak budur.
    /// </summary>
    /// <param name="label">Satır etiketi.</param>
    /// <param name="aim">İzlenecek transform.</param>
    private void AppendAim(string label, Transform aim)
    {
        if (aim == null)
        {
            Row(label, "atanmamis", false);
            return;
        }

        if (!aim.gameObject.activeInHierarchy)
        {
            Row(label, "nesne KAPALI (modality manager kapatmis olabilir)", false);
            return;
        }

        float distance = aim.localPosition.magnitude;
        bool moved = distance > 0.05f;

        // Uyarı işaretinde "<" kullanılamaz; TMP onu bozuk bir zengin metin
        // etiketi sanıp satırın kalanını yutuyor.
        Row(label, "local=" + Fmt(aim.localPosition) + (moved ? "" : "   !! YAZILMIYOR !!"), moved);

        // Pozu kim suruyor?
        var driver = aim.GetComponent<HarmonyHandAimPose>();
        if (driver == null)
        {
            Row("   surucu", "HarmonyHandAimPose YOK", false);
        }
        else
        {
            Row("   surucu", "kaynak=" + driver.ConfiguredSource +
                             "  MLeylem=" + (driver.AimActionResolved ? "bulundu" : "YOK") +
                             "  poz=" + driver.HasAimPose,
                driver.IsDriving && driver.HasAimPose);
        }

        var tpd = aim.GetComponent<UnityEngine.InputSystem.XR.TrackedPoseDriver>();
        if (tpd != null)
            Row("   TrackedPoseDriver", tpd.enabled ? "ACIK" : "kapali (devralindi)", !tpd.enabled);

        // Görünen çizginin BAŞLANGICI Aim Pose değil, pinch noktası zinciridir
        // (CurveVisualController.overrideLineOrigin -> Pinch Visual Offset).
        // Aim Pose doğru olsa bile bu nesne kıpırdamıyorsa ışın kafadan çıkar.
        Transform hand = aim.parent;
        if (hand == null) return;

        var pinch = hand.Find("Pinch Point Stabilized");
        if (pinch == null)
        {
            Row("   cizgi baslangici", "Pinch Point Stabilized yok", false);
            return;
        }

        bool pinchMoved = pinch.localPosition.sqrMagnitude > 1e-4f;
        Row("   cizgi baslangici", "local=" + Fmt(pinch.localPosition) +
            (pinchMoved ? "" : "   !! KIPIRDAMIYOR !!"), pinchMoved);
    }

    /// <summary>Etiket + değer satırı; durum rengiyle.</summary>
    /// <param name="label">Sol sütun.</param>
    /// <param name="value">Sağ sütun.</param>
    /// <param name="ok">Yeşil mi kırmızı mı yazılacağı.</param>
    private void Row(string label, string value, bool ok)
    {
        builder.Append("<color=#94a3b8>").Append(label).Append(":</color> ")
               .Append(ok ? "<color=#10b981>" : "<color=#ef4444>").Append(value).Append("</color>\n");
    }

    private static string Fmt(Vector3 v)
        => "(" + v.x.ToString("F2") + ", " + v.y.ToString("F2") + ", " + v.z.ToString("F2") + ")";
}
