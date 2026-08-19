using UnityEngine;
using UnityEngine.XR;
using System.Collections.Generic;

/// <summary>
/// ML2 cihaz pozunu nesne pozuyla birlestirerek dunya uzayinda sabit konum saglar.
/// Tracking kaybi durumunda anchor'u korur ve yeniden tanima ile snap yapar.
/// </summary>
public class SLAMPoseAnchor : MonoBehaviour
{
    [Header("Anchor Ayarlari")]
    [SerializeField] private float anchorCreateConfidence = 0.6f;
    [SerializeField] private float anchorSnapDistance = 0.5f;
    [SerializeField] private float driftCheckInterval = 30f;
    [SerializeField] private float driftThreshold = 0.3f;

    [Header("Debug")]
    [SerializeField] private bool showDebug;

    private Vector3 anchorWorldPosition;
    private Quaternion anchorWorldRotation;
    private bool hasAnchor;
    private float lastDriftCheckTime;
    private int observationCount;

    // XR cihaz referansi
    private InputDevice headDevice;
    private bool headDeviceFound;

    /// <summary> Anchor olusturuldu mu? </summary>
    public bool HasAnchor => hasAnchor;

    /// <summary> Anchor'un dunya pozisyonu. </summary>
    public Vector3 WorldPosition => anchorWorldPosition;

    /// <summary> Anchor'un dunya rotasyonu. </summary>
    public Quaternion WorldRotation => anchorWorldRotation;

    void Start()
    {
        FindHeadDevice();
    }

    /// <summary>
    /// Nesne pozunu cihaz pozuyla birlestirerek dunya uzayinda gunceller.
    /// </summary>
    /// <param name="objectCameraPose">Nesnenin kameraya gore pozu (pozisyon)</param>
    /// <param name="objectCameraRotation">Nesnenin kameraya gore rotasyonu</param>
    /// <param name="confidence">Poz tahmini guvenilirligi [0-1]</param>
    public void UpdatePose(Vector3 objectCameraPose, Quaternion objectCameraRotation,
                           float confidence = 1.0f)
    {
        // Cihaz pozunu al
        Vector3 devicePos;
        Quaternion deviceRot;
        if (!GetDevicePose(out devicePos, out deviceRot))
        {
            // XR cihaz pozu alinamiyorsa kamera transform kullan
            Camera cam = Camera.main;
            if (cam != null)
            {
                devicePos = cam.transform.position;
                deviceRot = cam.transform.rotation;
            }
            else
            {
                return;
            }
        }

        // Dunya pozu hesapla: world = device * camera_object
        Vector3 worldPos = devicePos + deviceRot * objectCameraPose;
        Quaternion worldRot = deviceRot * objectCameraRotation;

        if (!hasAnchor)
        {
            // Ilk anchor olusturma (yeterli guven gerekli)
            if (confidence >= anchorCreateConfidence)
            {
                anchorWorldPosition = worldPos;
                anchorWorldRotation = worldRot;
                hasAnchor = true;
                observationCount = 1;
                lastDriftCheckTime = Time.time;

                if (showDebug)
                    Debug.Log($"[SLAMPoseAnchor] Anchor olusturuldu: pos={worldPos}");
            }
        }
        else
        {
            // Mevcut anchor'a snap kontrolu
            float dist = Vector3.Distance(worldPos, anchorWorldPosition);

            if (dist < anchorSnapDistance)
            {
                // Anchor'u yumusak guncelle (gozlem ortalamasi)
                observationCount++;
                float alpha = 1.0f / Mathf.Min(observationCount, 30);
                anchorWorldPosition = Vector3.Lerp(anchorWorldPosition, worldPos, alpha);
                anchorWorldRotation = Quaternion.Slerp(anchorWorldRotation, worldRot, alpha);
            }
            else if (confidence > anchorCreateConfidence * 1.2f)
            {
                // Cok farkli konum ama yuksek guven: yeni anchor
                anchorWorldPosition = worldPos;
                anchorWorldRotation = worldRot;
                observationCount = 1;

                if (showDebug)
                    Debug.Log($"[SLAMPoseAnchor] Anchor yeniden olusturuldu (drift={dist:F2}m)");
            }

            // Periyodik drift kontrolu
            if (Time.time - lastDriftCheckTime > driftCheckInterval)
            {
                lastDriftCheckTime = Time.time;
                if (dist > driftThreshold && showDebug)
                    Debug.LogWarning($"[SLAMPoseAnchor] Drift tespit edildi: {dist:F2}m");
            }
        }
    }

    /// <summary> Anchor'u sifirlar. </summary>
    public void ResetAnchor()
    {
        hasAnchor = false;
        observationCount = 0;
    }

    /// <summary>
    /// Tracking kaybindan sonra en yakin anchor'a snap yapar.
    /// </summary>
    /// <param name="currentWorldPos">Mevcut tahmin edilen dunya pozisyonu</param>
    /// <returns>Snap basarili mi?</returns>
    public bool TryRelocalize(Vector3 currentWorldPos)
    {
        if (!hasAnchor) return false;

        float dist = Vector3.Distance(currentWorldPos, anchorWorldPosition);
        if (dist < anchorSnapDistance * 2f)
        {
            if (showDebug)
                Debug.Log($"[SLAMPoseAnchor] Relocalization basarili (dist={dist:F2}m)");
            return true;
        }

        return false;
    }

    private bool GetDevicePose(out Vector3 position, out Quaternion rotation)
    {
        position = Vector3.zero;
        rotation = Quaternion.identity;

        if (!headDeviceFound)
            FindHeadDevice();

        if (!headDeviceFound)
            return false;

        bool hasPos = headDevice.TryGetFeatureValue(
            CommonUsages.devicePosition, out position);
        bool hasRot = headDevice.TryGetFeatureValue(
            CommonUsages.deviceRotation, out rotation);

        return hasPos && hasRot;
    }

    private void FindHeadDevice()
    {
        var devices = new List<InputDevice>();
        InputDevices.GetDevicesWithCharacteristics(
            InputDeviceCharacteristics.HeadMounted, devices);

        if (devices.Count > 0)
        {
            headDevice = devices[0];
            headDeviceFound = true;
        }
    }
}
