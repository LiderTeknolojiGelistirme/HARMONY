using System.Collections;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.UI;
using UnityEngine.XR.Interaction.Toolkit.Inputs;

/// <summary>
/// HarmonyWebUI panelindeki dort gorev buttonunu el takibi jestleriyle tetikler.
/// GestureDisplay ile ayni input aksiyonlarini kullanir: LeftHand/RightHand
/// action map'lerindeki Pinch/Grasp deger ve hazirlik sinyalleri.
///
/// Jest -> Button eslesmesi (varsayilan):
///   Acik el (OpenPalm)  -> Start Scan        (BtnStart)
///   Pinch               -> Confirm and Clean (BtnConfirm)
///   Pinch + Grasp       -> Scan Again        (BtnReinspect)
///   Yumruk (Fist)       -> Abort Mission     (BtnStop)
///
/// Tetikleme icin poz holdDuration boyunca sabit tutulmali; ayni jestin yeniden
/// tetiklenmesi icin once pozdan cikilmasi gerekir. Tetik sonrasi cooldown uygulanir.
/// </summary>
public class GestureButtonController : MonoBehaviour
{
    /// <summary>Tetiklenebilecek el jestleri.</summary>
    public enum HandGesture { None, OpenPalm, Pinch, Fist, PinchGrasp }

    [Header("Button Referanslari (bos birakilirsa panel altinda isimle bulunur)")]
    [SerializeField] private Button startScanButton;    // OpenPalm  -> BtnStart
    [SerializeField] private Button confirmCleanButton; // Pinch     -> BtnConfirm
    [SerializeField] private Button scanAgainButton;    // PinchGrasp-> BtnReinspect
    [SerializeField] private Button abortMissionButton; // Fist      -> BtnStop

    [Header("Zamanlama")]
    [Tooltip("Jestin tetiklenmesi icin sabit tutulmasi gereken sure (saniye).")]
    [SerializeField] private float holdDuration = 1.0f;
    [Tooltip("Acik el pozunu bos durustan ayirt etmek icin daha uzun tutma suresi.")]
    [SerializeField] private float openPalmHoldDuration = 2.0f;
    [Tooltip("Bir tetiklemeden sonra yeni jest kabul edilmeyen sure (saniye).")]
    [SerializeField] private float cooldownDuration = 1.5f;

    private InputActionMap leftHandMap;
    private InputActionMap rightHandMap;
    private bool initialized;

    private HandGesture currentGesture = HandGesture.None;
    private HandGesture firedGesture = HandGesture.None;
    private float holdTimer;
    private float cooldownTimer;

    /// <summary>Atanmamis button referanslarini panel cocuklarindan isimle doldurur.</summary>
    private void Awake()
    {
        if (startScanButton == null) startScanButton = FindButton("BtnStart");
        if (confirmCleanButton == null) confirmCleanButton = FindButton("BtnConfirm");
        if (scanAgainButton == null) scanAgainButton = FindButton("BtnReinspect");
        if (abortMissionButton == null) abortMissionButton = FindButton("BtnStop");
    }

    /// <summary>Her karede el pozunu okur, dwell/cooldown mantigini isletir.</summary>
    private void Update()
    {
        if (!initialized && !TryInitialize())
            return;

        if (cooldownTimer > 0f)
        {
            cooldownTimer -= Time.deltaTime;
            return;
        }

        HandGesture gesture = DetectGesture(rightHandMap);
        if (gesture == HandGesture.None)
            gesture = DetectGesture(leftHandMap);

        if (gesture != currentGesture)
        {
            currentGesture = gesture;
            holdTimer = 0f;
            // Poz degisti; ayni jestin yeniden tetiklenmesinin onunu ac
            if (gesture != firedGesture)
                firedGesture = HandGesture.None;
            return;
        }

        if (gesture == HandGesture.None || gesture == firedGesture)
            return;

        holdTimer += Time.deltaTime;
        float required = gesture == HandGesture.OpenPalm ? openPalmHoldDuration : holdDuration;
        if (holdTimer >= required)
        {
            firedGesture = gesture;
            holdTimer = 0f;
            cooldownTimer = cooldownDuration;
            TriggerButton(gesture);
        }
    }

    /// <summary>InputActionManager uzerinden LeftHand/RightHand action map'lerini bulur.</summary>
    private bool TryInitialize()
    {
        var ism = FindAnyObjectByType<InputActionManager>();
        if (ism == null || ism.actionAssets == null || ism.actionAssets.Count == 0)
            return false;

        foreach (var asset in ism.actionAssets)
        {
            var left = asset.FindActionMap("LeftHand");
            var right = asset.FindActionMap("RightHand");
            if (left != null && right != null)
            {
                leftHandMap = left;
                rightHandMap = right;
                initialized = true;
                Debug.Log("[GestureButtonController] Initialized with action maps from: " + asset.name);
                return true;
            }
        }

        Debug.LogWarning("[GestureButtonController] No LeftHand/RightHand action maps found in any action asset");
        return false;
    }

    /// <summary>
    /// Pinch/Grasp deger ve hazirlik sinyallerinden statik el pozunu cikarir.
    /// Esikler GestureDisplay.DetectGesture ile ayni tutulmustur.
    /// </summary>
    private HandGesture DetectGesture(InputActionMap handMap)
    {
        var pinchValueAction = handMap.FindAction("PinchValue");
        var pinchReadyAction = handMap.FindAction("PinchReady");
        var graspValueAction = handMap.FindAction("GraspValue");
        var graspReadyAction = handMap.FindAction("GraspReady");

        if (pinchValueAction == null || graspValueAction == null)
            return HandGesture.None;

        float pinchValue = pinchValueAction.ReadValue<float>();
        bool pinchReady = pinchReadyAction != null && pinchReadyAction.ReadValue<float>() > 0f;
        float graspValue = graspValueAction.ReadValue<float>();
        bool graspReady = graspReadyAction != null && graspReadyAction.ReadValue<float>() > 0f;

        if (!pinchReady && !graspReady)
            return HandGesture.None;

        if (graspValue > 0.8f && pinchValue < 0.3f)
            return HandGesture.Fist;

        if (pinchValue > 0.8f && graspValue < 0.3f)
            return HandGesture.Pinch;

        if (pinchValue > 0.8f && graspValue > 0.5f)
            return HandGesture.PinchGrasp;

        if (graspValue < 0.2f && pinchValue < 0.2f)
            return HandGesture.OpenPalm;

        return HandGesture.None;
    }

    /// <summary>Jeste karsilik gelen buttonu bulur ve tiklama olayini gonderir.</summary>
    private void TriggerButton(HandGesture gesture)
    {
        Button target = gesture switch
        {
            HandGesture.OpenPalm => startScanButton,
            HandGesture.Pinch => confirmCleanButton,
            HandGesture.PinchGrasp => scanAgainButton,
            HandGesture.Fist => abortMissionButton,
            _ => null
        };

        if (target == null || !target.gameObject.activeInHierarchy || !target.interactable)
        {
            Debug.Log($"[GestureButtonController] {gesture}: hedef button pasif ya da atanmamis, tiklama gonderilmedi.");
            return;
        }

        Debug.Log($"[GestureButtonController] {gesture} -> {target.gameObject.name}");
        StartCoroutine(ClickRoutine(target));
    }

    /// <summary>Buttona gercek tiklama gibi pointer down/up/click olaylarini sirayla gonderir.</summary>
    private IEnumerator ClickRoutine(Button target)
    {
        var eventData = new PointerEventData(EventSystem.current);
        ExecuteEvents.Execute(target.gameObject, eventData, ExecuteEvents.pointerDownHandler);
        yield return new WaitForSeconds(0.1f);
        ExecuteEvents.Execute(target.gameObject, eventData, ExecuteEvents.pointerUpHandler);
        ExecuteEvents.Execute(target.gameObject, eventData, ExecuteEvents.pointerClickHandler);
    }

    /// <summary>Panelin dogrudan cocuklari arasinda isimle Button arar.</summary>
    private Button FindButton(string childName)
    {
        Transform t = transform.Find(childName);
        return t != null ? t.GetComponent<Button>() : null;
    }
}
