using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.XR.Hands;

/// <summary>
/// El takibi eklemlerinden bir işaretleme (aim) pozu üretip bu nesnenin
/// transform'una yazar.
///
/// NEDEN GEREKLİ — XRI'nin hazır kurulumunda işaretleme pozu
/// <c>&lt;HandInteraction&gt;/pointer</c> yoluna bağlıdır; bu, OpenXR'ın
/// XR_EXT_hand_interaction eklentisinden gelir. Çalışma zamanı bu eklentiyi
/// sunmuyorsa action hiç ateşlemez, <see cref="UnityEngine.InputSystem.XR.TrackedPoseDriver"/>
/// nesneye hiçbir şey yazmaz ve nesne local (0,0,0)'da yani XR Origin'in
/// merkezinde kalır. Sonuç: ışın elden değil kafanın içinden çıkar ve el
/// hareket ettikçe kıpırdamaz.
///
/// Eklem verisi (XR_EXT_hand_tracking) ayrı bir yoldan geldiği için el takibi
/// çalışırken bile bu durum oluşabilir. Bu bileşen o boşluğu kapatır.
///
/// IŞIN GEOMETRİSİ — Quest ve Android XR'ın yaptığı gibi: ışın elden başlar
/// ama yönü omuzdan ele çizilen doğrudur. Yalnızca elin baktığı yön
/// kullanılsaydı bilek en ufak dönmede işaretçiyi metrelerce kaydırırdı;
/// omuz-el ekseni ise kolu bir işaret çubuğu gibi kullanmayı sağlar.
/// </summary>
[DefaultExecutionOrder(-29988)] // Pose sürücülerinden sonra, XRI etkileşim yöneticisinden (-105) önce
public class HarmonyHandAimPose : MonoBehaviour
{
    /// <summary>İşaretleme pozunun kaynağı.</summary>
    public enum PoseSource
    {
        /// <summary>
        /// Yerel sürücü pozu gerçekten yazıyorsa ona dokunma; yazmıyorsa
        /// devral. Karar cihazın varlığına değil, pozun yazılıp yazılmadığına
        /// bakılarak verilir — cihaz var olup pozun boş gelmesi mümkündür.
        /// </summary>
        Auto,

        /// <summary>Her durumda eklemlerden üret.</summary>
        HandJoints,

        /// <summary>Hiç üretme; yalnızca yerel pozu kullan (bileşen etkisiz kalır).</summary>
        NativeOnly,

        /// <summary>
        /// Magic Leap'in kendi girdi varlığındaki tek parça Pose action'ını
        /// (LeftHand/RightHand → "Aim") okuyup doğrudan transform'a yazar.
        /// MagicLeap_Examples/Hands.unity içindeki PinchGesture'ın yöntemi;
        /// TrackedPoseDriver devre dışı bırakılır.
        /// </summary>
        MagicLeapAimAction
    }

    [Header("El")]
    [Tooltip("Bu pozun ait olduğu el.")]
    [SerializeField] private Handedness handedness = Handedness.Right;

    [Tooltip("Pozun yazılacağı transform. Boş bırakılırsa bu nesne kullanılır.")]
    [SerializeField] private Transform aimTarget;

    [Header("Kaynaklar")]
    [Tooltip("Eklem pozları bu transform'un uzayında gelir. Boş bırakılırsa sahnedeki XROrigin bulunur.")]
    [SerializeField] private Transform trackingOrigin;

    [Tooltip("Omuz kestirimi için baş. Boş bırakılırsa ana kamera kullanılır.")]
    [SerializeField] private Transform head;

    [Header("Davranış")]
    [SerializeField] private PoseSource source = PoseSource.MagicLeapAimAction;

    [Tooltip("MagicLeapAimAction kipinde okunacak eylem haritası ve eylem adı. " +
             "MagicLeapInput.inputactions içindeki LeftHand/RightHand → Aim.")]
    [SerializeField] private string aimActionName = "Aim";

    [Tooltip("MagicLeapAimAction kipi poz üretemezse el eklemlerine düş.")]
    [SerializeField] private bool fallbackToJoints = true;

    [Tooltip("Işının başlayacağı eklem. İşaret parmağı boğumu bilekten daha " +
             "kararlı, parmak ucundan daha az titrektir.")]
    [SerializeField] private XRHandJointID originJoint = XRHandJointID.IndexProximal;

    [Tooltip("Başa göre omuz kestirimi [m]: (yanal, aşağı, geri). " +
             "X işareti ele göre otomatik çevrilir.")]
    [SerializeField] private Vector3 shoulderOffset = new Vector3(0.16f, -0.22f, -0.05f);

    [Tooltip("Işını elin biraz önünden başlat [m]. 0 ise boğumun tam üstünden çıkar.")]
    [SerializeField] private float originForwardOffset = 0.03f;

    [Tooltip("Poz yumuşatma hızı [1/s]. Yüksek değer daha çabuk ama daha titrek.")]
    [SerializeField] private float smoothing = 16f;

    [Tooltip("Auto kipinde: el izlenirken yerel sürücü bu kadar süre boyunca " +
             "konum yazmazsa poz devralınır [s].")]
    [SerializeField] private float takeoverDelay = 0.5f;

    [Header("Tanılama")]
    [Tooltip("Açılışta hangi işaretleme cihazlarının bulunduğunu konsola yaz. " +
             "Cihazda ışın hâlâ yanlışsa ilk bakılacak yer burasıdır.")]
    [SerializeField] private bool logAvailabilityOnce = true;

    // XR_EXT_hand_interaction / hand common poses cihazlarının Input System
    // düzen adları. Bunlardan biri varsa yerel poz zaten çalışıyordur.
    private static readonly string[] NativeAimLayouts = { "HandInteraction", "HandInteractionPoses", "MetaAimHand" };

    private static readonly List<XRHandSubsystem> SubsystemBuffer = new List<XRHandSubsystem>();

    private XRHandSubsystem subsystem;
    private bool hasPose;
    private bool logged;

    // Devralma kilitlenir: bir kez devraldıktan sonra geri dönülmez, yoksa
    // kendi yazdığımız konumu "yerel sürücü çalışıyor" sanıp yalpalarız.
    private bool takenOver;
    private float nativeIdleSince = -1f;

    // Devralınca kapatılan yerel sürücü. Kapatılmazsa onBeforeRender aşamasında
    // dönüşü tekrar üzerine yazar ve poz titrer.
    private UnityEngine.InputSystem.XR.TrackedPoseDriver nativeDriver;

    // Magic Leap girdi varlığındaki tek parça Pose eylemi.
    private InputAction aimAction;
    private bool aimActionLogged;

    // Yerel cihaz taraması her karede yapılmaz; cihazlar sonradan da gelebildiği
    // için belirli aralıklarla tazelenir.
    private const float NativeCheckInterval = 1f;
    private float nextNativeCheck;
    private bool nativeAvailable;

    /// <summary>Bu karede geçerli bir işaretleme pozu üretildi mi?</summary>
    public bool HasAimPose => hasPose;

    /// <summary>Pozu şu anda bu bileşen mi sürüyor?</summary>
    public bool IsDriving =>
        source == PoseSource.HandJoints ||
        source == PoseSource.MagicLeapAimAction ||
        takenOver;

    /// <summary>MagicLeapAimAction kipinde eylem bulundu mu?</summary>
    public bool AimActionResolved => aimAction != null;

    /// <summary>Seçili poz kaynağı; tanılama için.</summary>
    public PoseSource ConfiguredSource => source;

    /// <summary>
    /// Baş ve işaret parmağı arasından ölçülen sıkıştırma (pinch) gücü, 0–1.
    /// Girdi sistemine bağlı değildir; yalnızca okunabilir bir ölçümdür.
    /// </summary>
    public float PinchStrength { get; private set; }

    private void Awake()
    {
        if (aimTarget == null) aimTarget = transform;

        if (trackingOrigin == null)
        {
            var origin = FindObjectOfType<Unity.XR.CoreUtils.XROrigin>();
            if (origin != null) trackingOrigin = origin.transform;
        }

        if (head == null && Camera.main != null) head = Camera.main.transform;

        nativeDriver = aimTarget.GetComponent<UnityEngine.InputSystem.XR.TrackedPoseDriver>();
    }

    private void Update()
    {
        if (source == PoseSource.NativeOnly) return;

        // Tanılama, alt sistem bulunamasa da bir kez yazılmalı; "el takibi
        // çalışmıyor" ile "işaretleme pozu yok" ayrı sorunlardır.
        RefreshNativeAvailability();

        if (source == PoseSource.MagicLeapAimAction)
        {
            EnsureNativeDriverDisabled();

            if (TryDriveFromAimAction()) return;
            if (!fallbackToJoints) { hasPose = false; return; }
            // Eylem poz vermiyorsa aşağıdaki eklem yoluna düşülür.
        }

        if (!TryGetSubsystem()) return;

        XRHand hand = handedness == Handedness.Left ? subsystem.leftHand : subsystem.rightHand;
        if (!hand.isTracked)
        {
            hasPose = false;
            return;
        }

        if (source != PoseSource.MagicLeapAimAction && !ShouldDrive()) return;

        UpdatePinch(hand);

        if (!TryGetJointWorldPose(hand, originJoint, out Pose jointPose))
        {
            hasPose = false;
            return;
        }

        ApplyAim(jointPose);
        hasPose = true;
    }

    /// <summary>
    /// Magic Leap'in girdi varlığındaki tek parça Pose eylemini okuyup pozu
    /// doğrudan yazar.
    ///
    /// MagicLeap_Examples/Hands.unity içindeki PinchGesture bunu yapıyor:
    /// <c>actionMap.FindAction("Pinch").ReadValue&lt;PoseState&gt;()</c> ve
    /// ardından SetPositionAndRotation. Tracking state bitlerine bakılmaz —
    /// ML2'de cihaz düzeyindeki bayrak grip pozunu yansıttığı için pointer
    /// konumunu yanlışlıkla eliyor.
    ///
    /// Poz, XR Origin uzayında gelir; nesne Camera Offset'in altında olduğu
    /// için yerel konum/dönüş olarak yazılır.
    /// </summary>
    /// <returns>Geçerli bir poz yazıldıysa true.</returns>
    private bool TryDriveFromAimAction()
    {
        if (!TryResolveAimAction()) return false;

        var pose = aimAction.ReadValue<UnityEngine.InputSystem.XR.PoseState>();

        // Konum hiç gelmiyorsa (hep sıfır) bu yol işe yaramıyor demektir;
        // çağıran taraf eklemlere düşebilsin diye başarısız dönülür.
        if (pose.position.sqrMagnitude < 1e-8f && !pose.isTracked) return false;

        // Poz XR Origin uzayında gelir. localPosition yazmak, nesnenin ata
        // zincirinin XR Origin'e göre birim olmasına bel bağlar — bu sahnede
        // öyle, ama Camera Offset'e bir ofset verildiği anda (tracking origin
        // mode = Floor ya da CameraYOffset ≠ 0) poz tam o kadar kayardı.
        // Dünya uzayına çevirip yazmak hiyerarşiden bağımsızdır.
        if (trackingOrigin != null)
        {
            Pose world = new Pose(pose.position, pose.rotation)
                .GetTransformedBy(new Pose(trackingOrigin.position, trackingOrigin.rotation));

            aimTarget.SetPositionAndRotation(world.position, world.rotation);
        }
        else
        {
            aimTarget.localPosition = pose.position;
            aimTarget.localRotation = pose.rotation;
        }

        hasPose = true;

        if (!aimActionLogged)
        {
            aimActionLogged = true;
            Debug.Log("HarmonyHandAimPose (" + handedness + "): poz ML '" +
                      aimAction.actionMap.name + "/" + aimAction.name + "' eyleminden suruluyor. " +
                      "isTracked=" + pose.isTracked + " pos=" + pose.position);
        }

        return true;
    }

    /// <summary>
    /// Aim eylemini sahnedeki InputActionManager'ın varlıkları içinde arar.
    /// </summary>
    /// <returns>Eylem bulunduysa true.</returns>
    private bool TryResolveAimAction()
    {
        if (aimAction != null) return true;

        var manager = FindObjectOfType<UnityEngine.XR.Interaction.Toolkit.Inputs.InputActionManager>();
        if (manager == null) return false;

        string mapName = handedness == Handedness.Left ? "LeftHand" : "RightHand";

        foreach (var asset in manager.actionAssets)
        {
            if (asset == null) continue;

            var map = asset.FindActionMap(mapName);
            if (map == null) continue;

            var action = map.FindAction(aimActionName);
            if (action == null) continue;

            // InputActionManager haritaları etkinleştirir, ama bileşen sırası
            // garanti değil; kendi eylemimizi burada da açıyoruz.
            if (!action.enabled) action.Enable();

            aimAction = action;
            return true;
        }

        return false;
    }

    /// <summary>
    /// Pozu bu bileşenin mi süreceğini belirler.
    ///
    /// Auto kipinde ölçüt "yerel cihaz var mı" değil, "yerel sürücü konumu
    /// gerçekten yazıyor mu"dur. ML2'de <c>HandInteraction</c> cihazı var ama
    /// tracking state'inde Position biti gelmiyor; TrackedPoseDriver bu durumda
    /// yalnızca dönüşü uygular (TrackedPoseDriver.cs:587-598) ve nesne
    /// origin'de kalır. Cihazın varlığına bakan bir ölçüt bu tuzağa düşer.
    /// </summary>
    /// <returns>Poz üretilecekse true.</returns>
    private bool ShouldDrive()
    {
        if (source == PoseSource.HandJoints)
        {
            EnsureNativeDriverDisabled();
            return true;
        }

        if (takenOver) return true;

        // El izleniyor ama nesne hâlâ origin'de duruyorsa yerel sürücü konum
        // yazmıyor demektir.
        bool nativeIdle = aimTarget.localPosition.sqrMagnitude < 1e-4f;

        if (!nativeIdle)
        {
            nativeIdleSince = -1f;
            return false;
        }

        if (nativeIdleSince < 0f) nativeIdleSince = Time.unscaledTime;
        if (Time.unscaledTime - nativeIdleSince < takeoverDelay) return false;

        takenOver = true;
        EnsureNativeDriverDisabled();
        Debug.Log("HarmonyHandAimPose (" + handedness + "): yerel surucu konum yazmiyor, " +
                  "poz el eklemlerinden devralindi.");
        return true;
    }

    /// <summary>
    /// Yerel poz sürücüsünü kapatır. Açık kalırsa render öncesi aşamada
    /// dönüşü tekrar yazar ve iki kaynak birbiriyle çakışır.
    /// </summary>
    private void EnsureNativeDriverDisabled()
    {
        if (nativeDriver == null || !nativeDriver.enabled) return;
        nativeDriver.enabled = false;
    }

    /// <summary>
    /// Eklem pozundan ışının başlangıcını ve yönünü hesaplayıp hedefe yazar.
    /// </summary>
    /// <param name="jointPose">Dünya uzayındaki kaynak eklem pozu.</param>
    private void ApplyAim(Pose jointPose)
    {
        Vector3 shoulder = EstimateShoulder();
        Vector3 direction = jointPose.position - shoulder;

        if (direction.sqrMagnitude < 1e-6f) return;
        direction.Normalize();

        Vector3 targetPosition = jointPose.position + direction * originForwardOffset;
        Quaternion targetRotation = Quaternion.LookRotation(direction, Vector3.up);

        if (smoothing > 0f && hasPose)
        {
            float k = 1f - Mathf.Exp(-smoothing * Time.deltaTime);
            aimTarget.SetPositionAndRotation(
                Vector3.Lerp(aimTarget.position, targetPosition, k),
                Quaternion.Slerp(aimTarget.rotation, targetRotation, k));
        }
        else
        {
            // İlk kare ya da yumuşatma kapalı: doğrudan otur, yoksa el ilk
            // göründüğünde ışın kafadan elin yanına doğru süzülür.
            aimTarget.SetPositionAndRotation(targetPosition, targetRotation);
        }
    }

    /// <summary>
    /// Başa göre omuz konumunu kestirir. Gerçek omuz izlenmediği için ışının
    /// pivotu buradan türetilir.
    /// </summary>
    private Vector3 EstimateShoulder()
    {
        if (head == null) return aimTarget.position - Vector3.forward;

        float side = handedness == Handedness.Left ? -shoulderOffset.x : shoulderOffset.x;
        Vector3 local = new Vector3(side, shoulderOffset.y, shoulderOffset.z);

        return head.position + head.rotation * local;
    }

    /// <summary>
    /// Başparmak ve işaret parmağı uçları arasındaki mesafeden sıkıştırma
    /// gücünü ölçer.
    /// </summary>
    /// <param name="hand">Ölçülecek el.</param>
    private void UpdatePinch(XRHand hand)
    {
        Pose thumb, index;
        if (!TryGetJointWorldPose(hand, XRHandJointID.ThumbTip, out thumb) ||
            !TryGetJointWorldPose(hand, XRHandJointID.IndexTip, out index))
        {
            PinchStrength = 0f;
            return;
        }

        // 5 cm açık, 2 cm kapalı kabul edilir; arası doğrusal.
        float distance = Vector3.Distance(thumb.position, index.position);
        PinchStrength = Mathf.Clamp01(Mathf.InverseLerp(0.05f, 0.02f, distance));
    }

    /// <summary>
    /// Eklem pozunu dünya uzayında verir. Eklem pozları XR Origin uzayındadır.
    /// </summary>
    /// <param name="hand">Kaynak el.</param>
    /// <param name="jointId">İstenen eklem.</param>
    /// <param name="worldPose">Bulunan dünya pozu.</param>
    /// <returns>Eklem izleniyorsa true.</returns>
    private bool TryGetJointWorldPose(XRHand hand, XRHandJointID jointId, out Pose worldPose)
    {
        worldPose = default;

        Pose local;
        if (!hand.GetJoint(jointId).TryGetPose(out local)) return false;

        if (trackingOrigin == null)
        {
            worldPose = local;
            return true;
        }

        worldPose = local.GetTransformedBy(new Pose(trackingOrigin.position, trackingOrigin.rotation));
        return true;
    }

    /// <summary>Çalışan el takibi alt sistemini bulur.</summary>
    private bool TryGetSubsystem()
    {
        if (subsystem != null && subsystem.running) return true;

        SubsystemManager.GetSubsystems(SubsystemBuffer);
        for (int i = 0; i < SubsystemBuffer.Count; i++)
        {
            if (!SubsystemBuffer[i].running) continue;
            subsystem = SubsystemBuffer[i];
            return true;
        }

        return false;
    }

    /// <summary>
    /// Çalışma zamanının kendi işaretleme cihazını sunup sunmadığını tazeler.
    /// Cihazlar oturum başladıktan sonra da belirebildiği için aralıklı bakılır.
    /// </summary>
    private void RefreshNativeAvailability()
    {
        if (Time.unscaledTime < nextNativeCheck) return;
        nextNativeCheck = Time.unscaledTime + NativeCheckInterval;

        nativeAvailable = false;

        foreach (InputDevice device in InputSystem.devices)
        {
            if (!MatchesHandedness(device)) continue;

            for (int i = 0; i < NativeAimLayouts.Length; i++)
            {
                if (!InputSystem.IsFirstLayoutBasedOnSecond(device.layout, NativeAimLayouts[i])) continue;
                nativeAvailable = true;
                break;
            }

            if (nativeAvailable) break;
        }

        if (!logAvailabilityOnce || logged) return;
        logged = true;

        bool subsystemRunning = TryGetSubsystem();

        var sb = new System.Text.StringBuilder();
        sb.Append("HarmonyHandAimPose (").Append(handedness).Append("): ")
          .Append("el takibi alt sistemi ").Append(subsystemRunning ? "CALISIYOR" : "YOK")
          .Append(" | yerel isaret cihazi ").Append(nativeAvailable ? "VAR" : "YOK")
          .Append(" -> poz kaynagi = ")
          .Append(source == PoseSource.Auto && nativeAvailable ? "yerel" : "el eklemleri")
          .Append(". Input System cihazlari: ");

        foreach (InputDevice device in InputSystem.devices)
            sb.Append('[').Append(device.layout).Append(" '").Append(device.name).Append("'] ");

        Debug.Log(sb.ToString());
    }

    /// <summary>Cihaz bu elin cihazı mı?</summary>
    /// <param name="device">Sınanacak cihaz.</param>
    private bool MatchesHandedness(InputDevice device)
    {
        string usage = handedness == Handedness.Left ? "LeftHand" : "RightHand";

        foreach (var u in device.usages)
            if (u == usage) return true;

        return false;
    }
}
