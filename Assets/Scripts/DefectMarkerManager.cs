using System.Collections.Generic;
using TMPro;
using UnityEngine;

public class DefectMarkerManager : MonoBehaviour
{
    [Header("Data Source")]
    [SerializeField] private MockRosDefectDataPublisher dataPublisher;
    [SerializeField] private bool convertFromRosCoordinates = true;
    [SerializeField] private Transform visualizationRoot;
    [SerializeField] private Vector3 worldOffset = Vector3.zero;

    [Header("Marker Ayarları")]
    [SerializeField] private GameObject defectIconPrefab;
    [SerializeField] private Transform markersParent;
    [SerializeField] private bool orientToCamera = true;
    [SerializeField] private Camera targetCamera;
    [SerializeField] private float iconScaleMultiplier = 1f;

    [Header("Severity Renkleri")]
    [SerializeField] private Color lowColor = new Color(1f, 0.9f, 0.2f);
    [SerializeField] private Color mediumColor = new Color(1f, 0.55f, 0f);
    [SerializeField] private Color highColor = new Color(1f, 0.15f, 0.15f);

    private readonly Dictionary<string, GameObject> activeMarkers = new();
    private readonly Dictionary<string, Vector3> markerBaseScales = new();
    private readonly HashSet<string> frameIds = new();

    private void Awake()
    {
        if (targetCamera == null)
            targetCamera = Camera.main;
    }

    private void OnEnable()
    {
        if (dataPublisher != null)
            dataPublisher.OnSceneDataPublished += HandleSceneData;
    }

    private void OnDisable()
    {
        if (dataPublisher != null)
            dataPublisher.OnSceneDataPublished -= HandleSceneData;
    }

    private void LateUpdate()
    {
        if (!orientToCamera || targetCamera == null)
            return;

        foreach (GameObject marker in activeMarkers.Values)
        {
            if (marker == null)
                continue;

            marker.transform.LookAt(targetCamera.transform);
            marker.transform.Rotate(0f, 180f, 0f);
        }
    }

    private void HandleSceneData(MockRosSceneData data)
    {
        if (data == null)
            return;

        frameIds.Clear();

        foreach (DefectPointData defect in data.defectPoints)
        {
            if (string.IsNullOrEmpty(defect.id))
                continue;

            frameIds.Add(defect.id);
            UpdateOrCreateMarker(defect);
        }

        RemoveMissingMarkers();
    }

    private void UpdateOrCreateMarker(DefectPointData defect)
    {
        if (!activeMarkers.TryGetValue(defect.id, out GameObject marker) || marker == null)
        {
            marker = CreateMarker(defect.id);
            activeMarkers[defect.id] = marker;
        }

        Vector3 worldPos = ResolveWorldPosition(defect.rosPosition.ToUnityPosition(convertFromRosCoordinates));
        marker.transform.position = worldPos;

        if (!markerBaseScales.TryGetValue(defect.id, out Vector3 baseScale))
            baseScale = Vector3.one;

        float scaleValue = Mathf.Max(0.001f, defect.iconScale * iconScaleMultiplier);
        marker.transform.localScale = baseScale * scaleValue;

        Color severityColor = GetSeverityColor(defect.severity);
        SetMarkerColor(marker, severityColor);
        SetMarkerLabel(marker, defect.id);
    }

    private GameObject CreateMarker(string defectId)
    {
        GameObject marker;
        if (defectIconPrefab != null)
        {
            marker = Instantiate(defectIconPrefab, markersParent);
        }
        else
        {
            marker = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            marker.name = $"DefectMarker_{defectId}";
            marker.transform.SetParent(markersParent, true);
            Collider col = marker.GetComponent<Collider>();
            if (col != null)
                Destroy(col);
        }

        markerBaseScales[defectId] = marker.transform.localScale;
        return marker;
    }

    private Vector3 ResolveWorldPosition(Vector3 localOrWorld)
    {
        Vector3 pos = localOrWorld;
        if (visualizationRoot != null)
            pos = visualizationRoot.TransformPoint(pos);
        return pos + worldOffset;
    }

    private void SetMarkerColor(GameObject marker, Color color)
    {
        Renderer[] renderers = marker.GetComponentsInChildren<Renderer>(true);
        foreach (Renderer renderer in renderers)
        {
            if (renderer.sharedMaterial == null || renderer.sharedMaterial.shader == null || !renderer.sharedMaterial.shader.isSupported)
                renderer.material = CreateOpaqueColorMaterial(color);

            if (renderer.material != null)
                ApplyColor(renderer.material, color);
        }
    }

    private Material CreateOpaqueColorMaterial(Color color)
    {
        string[] shaderNames =
        {
            "Universal Render Pipeline/Lit",
            "Universal Render Pipeline/Unlit",
            "HDRP/Lit",
            "HDRP/Unlit",
            "Standard",
            "Unlit/Color",
            "Sprites/Default"
        };

        Shader shader = null;
        for (int i = 0; i < shaderNames.Length; i++)
        {
            shader = Shader.Find(shaderNames[i]);
            if (shader != null)
                break;
        }

        if (shader == null)
            return null;

        Material mat = new Material(shader);
        ApplyColor(mat, color);
        return mat;
    }

    private void ApplyColor(Material material, Color color)
    {
        if (material == null)
            return;

        if (material.HasProperty("_BaseColor"))
            material.SetColor("_BaseColor", color);
        if (material.HasProperty("_Color"))
            material.SetColor("_Color", color);
    }

    private void SetMarkerLabel(GameObject marker, string text)
    {
        TMP_Text tmpUi = marker.GetComponentInChildren<TMP_Text>(true);
        if (tmpUi != null)
            tmpUi.text = text;

        TextMeshPro tmp3D = marker.GetComponentInChildren<TextMeshPro>(true);
        if (tmp3D != null)
            tmp3D.text = text;
    }

    private Color GetSeverityColor(DefectSeverity severity)
    {
        switch (severity)
        {
            case DefectSeverity.Low:
                return lowColor;
            case DefectSeverity.Medium:
                return mediumColor;
            case DefectSeverity.High:
                return highColor;
            default:
                return mediumColor;
        }
    }

    private void RemoveMissingMarkers()
    {
        var idsToRemove = new List<string>();
        foreach (string markerId in activeMarkers.Keys)
        {
            if (!frameIds.Contains(markerId))
                idsToRemove.Add(markerId);
        }

        foreach (string id in idsToRemove)
        {
            if (activeMarkers[id] != null)
                Destroy(activeMarkers[id]);
            activeMarkers.Remove(id);
            markerBaseScales.Remove(id);
        }
    }
}

