using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.XR.Interaction.Toolkit.Inputs;
using TMPro;

public class GestureDisplay : MonoBehaviour
{
    [SerializeField] private TextMeshProUGUI messageText;
    [SerializeField] private GameObject messageBox;

    private InputActionMap leftHandMap;
    private InputActionMap rightHandMap;
    private string currentPoseName;
    private bool initialized;

    private void Update()
    {
        if (!initialized)
        {
            if (!TryInitialize())
                return;
        }

        string poseName = DetectGesture(rightHandMap) ?? DetectGesture(leftHandMap);

        if (poseName != currentPoseName)
        {
            currentPoseName = poseName;
            Debug.Log($"[GestureDisplay] Pose: {poseName ?? "None"}");
            UpdateDisplay();
        }
    }

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
                Debug.Log("[GestureDisplay] Initialized with action maps from: " + asset.name);
                return true;
            }
        }

        Debug.LogWarning("[GestureDisplay] No LeftHand/RightHand action maps found in any action asset");
        return false;
    }

    private string DetectGesture(InputActionMap handMap)
    {
        var pinchValueAction = handMap.FindAction("PinchValue");
        var pinchReadyAction = handMap.FindAction("PinchReady");
        var graspValueAction = handMap.FindAction("GraspValue");
        var graspReadyAction = handMap.FindAction("GraspReady");

        if (pinchValueAction == null || graspValueAction == null)
            return null;

        float pinchValue = pinchValueAction.ReadValue<float>();
        bool pinchReady = pinchReadyAction != null && pinchReadyAction.ReadValue<float>() > 0f;
        float graspValue = graspValueAction.ReadValue<float>();
        bool graspReady = graspReadyAction != null && graspReadyAction.ReadValue<float>() > 0f;

        if (!pinchReady && !graspReady)
            return null;

        if (graspValue > 0.8f && pinchValue < 0.3f)
            return "Fist";

        if (pinchValue > 0.8f && graspValue < 0.3f)
            return "Pinch";

        if (pinchValue > 0.8f && graspValue > 0.5f)
            return "Grasp + Pinch";

        if (graspValue > 0.5f)
            return "Grasp";

        if (graspValue < 0.2f && pinchValue < 0.2f && (pinchReady || graspReady))
            return "Open";

        return null;
    }

    private void UpdateDisplay()
    {
        if (currentPoseName == null)
        {
            messageBox.SetActive(false);
            return;
        }

        messageBox.SetActive(true);
        if (messageText != null)
            messageText.text = currentPoseName;
    }
}
