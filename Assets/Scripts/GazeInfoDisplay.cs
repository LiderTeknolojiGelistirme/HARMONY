using MagicLeap.Android;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.XR;
using UnityEngine.XR.OpenXR.Features.Interactions;
using TMPro;
using DG.Tweening;

public class GazeInfoDisplay : MonoBehaviour
{
    [SerializeField] private GameObject labelPrefab;
    [SerializeField] private float labelOffsetY = 0.15f;
    [SerializeField] private float gazeSmoothing = 8f;
    [SerializeField] private float trailThreshold = 0.03f;
    [SerializeField] private float spawnStartScaleMultiplier = 0.2f;
    [SerializeField] private float spawnScaleDuration = 0.22f;
    [SerializeField] private float jiggleStrength = 0.08f;
    [SerializeField] private float jiggleDuration = 0.25f;
    [SerializeField] private int jiggleVibrato = 10;
    [SerializeField] private float jiggleElasticity = 0.8f;

    private List<InputDevice> inputDeviceList = new();
    private InputDevice eyeTracking;
    private Camera mainCamera;
    private bool permissionGranted;

    private GameObject activeLabel;
    private Transform lastHitTarget;
    private Vector3 smoothedGazeDirection;
    private Vector3 labelDefaultScale;
    private Sequence labelSpawnSequence;

    private void Awake()
    {
        Permissions.RequestPermission(Permissions.EyeTracking, OnPermissionGranted, OnPermissionDenied);
    }

    private void Update()
    {
        if (!permissionGranted)
            return;

        if (!eyeTracking.isValid)
        {
            InputDevices.GetDevicesWithCharacteristics(InputDeviceCharacteristics.EyeTracking, inputDeviceList);
            eyeTracking = inputDeviceList.FirstOrDefault();
            if (!eyeTracking.isValid)
                return;
        }

        bool hasData = eyeTracking.TryGetFeatureValue(CommonUsages.isTracked, out bool isTracked);
        hasData &= eyeTracking.TryGetFeatureValue(EyeTrackingUsages.gazePosition, out Vector3 gazePos);
        hasData &= eyeTracking.TryGetFeatureValue(EyeTrackingUsages.gazeRotation, out Quaternion gazeRot);

        if (!isTracked || !hasData)
        {
            HideLabel();
            return;
        }

        Vector3 gazeDirection = gazeRot * Vector3.forward;
        smoothedGazeDirection = Vector3.Lerp(smoothedGazeDirection, gazeDirection, Time.deltaTime * gazeSmoothing);

        var ray = new Ray(mainCamera.transform.position, smoothedGazeDirection);
        if (Physics.Raycast(ray, out RaycastHit hit))
        {
            ShowLabel(hit.transform);
        }
        else
        {
            HideLabel();
        }
    }

    private void ShowLabel(Transform target)
    {
        bool isNewLabel = false;
        if (activeLabel == null)
        {
            activeLabel = Instantiate(labelPrefab);
            labelDefaultScale = activeLabel.transform.localScale;
            isNewLabel = true;
        }

        lastHitTarget = target;
        bool wasHidden = !activeLabel.activeSelf;
        activeLabel.SetActive(true);

        if (isNewLabel || wasHidden)
            PlayLabelSpawnAnimation();

        Vector3 labelPos = target.position + Vector3.up * labelOffsetY;
        activeLabel.transform.position = labelPos;
        activeLabel.transform.LookAt(mainCamera.transform);
        activeLabel.transform.Rotate(0, 180, 0);

        var tmp = activeLabel.GetComponentInChildren<TextMeshProUGUI>();
        if (tmp != null)
            tmp.text = target.gameObject.name;

        var tmpWorld = activeLabel.GetComponentInChildren<TextMeshPro>();
        if (tmpWorld != null)
            tmpWorld.text = target.gameObject.name;
    }

    private void HideLabel()
    {
        if (activeLabel != null)
        {
            labelSpawnSequence?.Kill();
            activeLabel.SetActive(false);
            activeLabel.transform.localScale = labelDefaultScale;
        }
        lastHitTarget = null;
    }

    private void PlayLabelSpawnAnimation()
    {
        if (activeLabel == null)
            return;

        labelSpawnSequence?.Kill();

        Transform labelTransform = activeLabel.transform;
        Vector3 startScale = labelDefaultScale * spawnStartScaleMultiplier;
        Vector3 punch = labelDefaultScale * jiggleStrength;

        labelTransform.localScale = startScale;

        labelSpawnSequence = DOTween.Sequence();
        labelSpawnSequence.Append(labelTransform.DOScale(labelDefaultScale, spawnScaleDuration).SetEase(Ease.OutCubic));
        labelSpawnSequence.Append(labelTransform.DOPunchScale(punch, jiggleDuration, jiggleVibrato, jiggleElasticity));
    }

    private void OnPermissionGranted(string permission)
    {
        permissionGranted = true;
        mainCamera = Camera.main;
        smoothedGazeDirection = mainCamera.transform.forward;
    }

    private void OnPermissionDenied(string permission)
    {
        Debug.LogError($"{permission} denied, GazeInfoDisplay won't function");
    }

    private void OnDisable()
    {
        labelSpawnSequence?.Kill();
    }
}
