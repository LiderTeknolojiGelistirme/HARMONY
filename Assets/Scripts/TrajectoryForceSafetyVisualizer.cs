using System.Collections.Generic;
using UnityEngine;

public class TrajectoryForceSafetyVisualizer : MonoBehaviour
{
    [Header("Data Source")]
    [SerializeField] private MockRosDefectDataPublisher dataPublisher;
    [SerializeField] private bool convertFromRosCoordinates = true;
    [SerializeField] private Transform visualizationRoot;
    [SerializeField] private Vector3 worldOffset = Vector3.zero;

    [Header("Trajectory")]
    [SerializeField] private LineRenderer trajectoryLine;
    [SerializeField] private float trajectoryLineWidth = 0.006f;
    [SerializeField] private Color trajectoryColor = Color.cyan;

    [Header("Force Vectors")]
    [SerializeField] private Material vectorMaterial;
    [SerializeField] private float vectorWidth = 0.004f;
    [SerializeField] private Color forceColor = new Color(1f, 0.25f, 0.25f);
    [SerializeField] private float forceScale = 1f;
    [SerializeField] private Transform vectorsParent;
    [SerializeField] private GameObject forceArrowPrefab;
    [SerializeField] private float forceArrowScale = 10f;
    [SerializeField] private bool arrowUsesUpAxis = true;
    [SerializeField] private float arrowSurfaceOffset = 0.005f;

    [Header("Safety Zones")]
    [SerializeField] private Material safetyZoneMaterial;
    [SerializeField] private Transform safetyZoneParent;
    [SerializeField] private float safetyZoneScaleMultiplier = 3f;

    private readonly Dictionary<string, LineRenderer> forceVectorMap = new();
    private readonly Dictionary<string, Transform> forceArrowMap = new();
    private readonly Dictionary<string, GameObject> safetyZoneMap = new();
    private readonly HashSet<string> liveForceIds = new();
    private readonly HashSet<string> liveZoneIds = new();
    private Renderer[] rootRenderers;

    private void Reset()
    {
        if (trajectoryLine == null)
            trajectoryLine = GetComponent<LineRenderer>();
    }

    private void Awake()
    {
        EnsureTrajectoryLine();
        CacheRootRenderers();
    }

    private void OnEnable()
    {
        CacheRootRenderers();
        if (dataPublisher != null)
            dataPublisher.OnSceneDataPublished += HandleSceneData;
    }

    private void OnDisable()
    {
        if (dataPublisher != null)
            dataPublisher.OnSceneDataPublished -= HandleSceneData;

        foreach (Transform arrow in forceArrowMap.Values)
        {
            if (arrow != null)
                Destroy(arrow.gameObject);
        }
        forceArrowMap.Clear();
    }

    private void HandleSceneData(MockRosSceneData data)
    {
        if (data == null)
            return;

        RenderTrajectory(data.trajectoryPoints);
        RenderForceVectors(data.forceVectors);
        RenderSafetyZones(data.safetyZones);
    }

    private void EnsureTrajectoryLine()
    {
        if (trajectoryLine == null)
        {
            GameObject lineObj = new GameObject("TrajectoryLine");
            lineObj.transform.SetParent(transform, false);
            trajectoryLine = lineObj.AddComponent<LineRenderer>();
        }

        if (trajectoryLine.material == null)
        {
            trajectoryLine.material = CreateOpaqueColorMaterial(trajectoryColor);
        }

        trajectoryLine.positionCount = 0;
        trajectoryLine.widthMultiplier = trajectoryLineWidth;
        trajectoryLine.startColor = trajectoryColor;
        trajectoryLine.endColor = trajectoryColor;
        trajectoryLine.useWorldSpace = true;
        trajectoryLine.loop = false;
    }

    private void RenderTrajectory(List<RosVector3> points)
    {
        if (trajectoryLine == null)
            return;

        int count = points != null ? points.Count : 0;
        trajectoryLine.positionCount = count;
        trajectoryLine.widthMultiplier = trajectoryLineWidth;
        trajectoryLine.startColor = trajectoryColor;
        trajectoryLine.endColor = trajectoryColor;

        for (int i = 0; i < count; i++)
        {
            Vector3 p = ResolveWorldPosition(points[i].ToUnityPosition(convertFromRosCoordinates));
            trajectoryLine.SetPosition(i, p);
        }
    }

    private void RenderForceVectors(List<ForceVectorData> vectors)
    {
        liveForceIds.Clear();

        if (vectors != null)
        {
            foreach (ForceVectorData vectorData in vectors)
            {
                if (string.IsNullOrEmpty(vectorData.id))
                    continue;

                liveForceIds.Add(vectorData.id);
                LineRenderer lr = GetOrCreateForceLine(vectorData.id);

                Vector3 start = ResolveWorldPosition(vectorData.rosStart.ToUnityPosition(convertFromRosCoordinates));
                Vector3 direction = vectorData.rosDirection.ToUnityDirection(convertFromRosCoordinates).normalized;
                Vector3 end = start + direction * (vectorData.magnitude * forceScale);

                lr.positionCount = 2;
                lr.widthMultiplier = vectorWidth;
                lr.startColor = forceColor;
                lr.endColor = forceColor;
                lr.SetPosition(0, start);
                lr.SetPosition(1, end);

                UpdateForceArrow(vectorData.id, start, end, direction);
            }
        }

        var forceIdsToRemove = new List<string>();
        foreach (string id in forceVectorMap.Keys)
        {
            if (!liveForceIds.Contains(id))
                forceIdsToRemove.Add(id);
        }

        foreach (string id in forceIdsToRemove)
        {
            if (forceVectorMap[id] != null)
                Destroy(forceVectorMap[id].gameObject);
            forceVectorMap.Remove(id);

            if (forceArrowMap.TryGetValue(id, out Transform arrow) && arrow != null)
                Destroy(arrow.gameObject);
            forceArrowMap.Remove(id);
        }
    }

    private LineRenderer GetOrCreateForceLine(string id)
    {
        if (forceVectorMap.TryGetValue(id, out LineRenderer existing) && existing != null)
            return existing;

        GameObject go = new GameObject($"ForceVector_{id}");
        go.transform.SetParent(vectorsParent != null ? vectorsParent : transform, false);
        LineRenderer lr = go.AddComponent<LineRenderer>();
        lr.useWorldSpace = true;

        if (vectorMaterial != null)
        {
            lr.material = vectorMaterial;
        }
        else
        {
            lr.material = CreateOpaqueColorMaterial(forceColor);
        }

        forceVectorMap[id] = lr;
        return lr;
    }

    private void UpdateForceArrow(string id, Vector3 start, Vector3 end, Vector3 direction)
    {
        if (forceArrowPrefab == null)
            return;

        if (!forceArrowMap.TryGetValue(id, out Transform arrow) || arrow == null)
        {
            GameObject arrowObj = Instantiate(forceArrowPrefab, vectorsParent != null ? vectorsParent : transform);
            arrow = arrowObj.transform;
            forceArrowMap[id] = arrow;
        }

        if (direction.sqrMagnitude < 0.000001f)
            direction = (end - start).normalized;
        if (direction.sqrMagnitude < 0.000001f)
            return;

        arrow.position = GetSurfacePointFromBounds(end, direction);
        arrow.localScale = Vector3.one * Mathf.Max(0.001f, forceArrowScale);

        if (arrowUsesUpAxis)
            arrow.up = direction;
        else
            arrow.forward = direction;
    }

    private void RenderSafetyZones(List<SafetyZoneData> zones)
    {
        liveZoneIds.Clear();

        if (zones != null)
        {
            foreach (SafetyZoneData zone in zones)
            {
                if (string.IsNullOrEmpty(zone.id))
                    continue;

                liveZoneIds.Add(zone.id);
                GameObject zoneObj = GetOrCreateSafetyZone(zone.id);

                Vector3 center = ResolveWorldPosition(zone.rosCenter.ToUnityPosition(convertFromRosCoordinates));
                zoneObj.transform.position = center;
                zoneObj.transform.localScale = Vector3.one * (zone.radius * 2f * Mathf.Max(0.01f, safetyZoneScaleMultiplier));

                Renderer renderer = zoneObj.GetComponent<Renderer>();
                if (renderer != null && renderer.material != null)
                {
                    Color c = zone.color;
                    if (c.a <= 0f)
                        c.a = 0.2f;
                    renderer.material.color = c;
                }
            }
        }

        var zoneIdsToRemove = new List<string>();
        foreach (string id in safetyZoneMap.Keys)
        {
            if (!liveZoneIds.Contains(id))
                zoneIdsToRemove.Add(id);
        }

        foreach (string id in zoneIdsToRemove)
        {
            if (safetyZoneMap[id] != null)
                Destroy(safetyZoneMap[id]);
            safetyZoneMap.Remove(id);
        }
    }

    private GameObject GetOrCreateSafetyZone(string id)
    {
        if (safetyZoneMap.TryGetValue(id, out GameObject existing) && existing != null)
            return existing;

        GameObject zoneObj = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        zoneObj.name = $"SafetyZone_{id}";
        zoneObj.transform.SetParent(safetyZoneParent != null ? safetyZoneParent : transform, false);

        Collider col = zoneObj.GetComponent<Collider>();
        if (col != null)
            Destroy(col);

        Renderer renderer = zoneObj.GetComponent<Renderer>();
        if (renderer != null)
        {
            if (safetyZoneMaterial != null)
            {
                renderer.material = safetyZoneMaterial;
            }
            else
            {
                renderer.material = CreateTransparentColorMaterial(new Color(0f, 0.8f, 1f, 0.2f));
            }
        }

        safetyZoneMap[id] = zoneObj;
        return zoneObj;
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

        Shader shader = FindFirstShader(shaderNames);
        if (shader == null)
            return null;

        Material material = new Material(shader);
        ApplyColor(material, color);
        return material;
    }

    private Material CreateTransparentColorMaterial(Color color)
    {
        string[] shaderNames =
        {
            "Universal Render Pipeline/Unlit",
            "Universal Render Pipeline/Lit",
            "HDRP/Unlit",
            "HDRP/Lit",
            "Unlit/Color",
            "Sprites/Default",
            "Standard"
        };

        Shader shader = FindFirstShader(shaderNames);
        if (shader == null)
            return null;

        Material material = new Material(shader);
        ApplyColor(material, color);
        ConfigureTransparency(material);
        return material;
    }

    private Shader FindFirstShader(string[] shaderNames)
    {
        for (int i = 0; i < shaderNames.Length; i++)
        {
            Shader shader = Shader.Find(shaderNames[i]);
            if (shader != null)
                return shader;
        }

        return null;
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

    private void ConfigureTransparency(Material material)
    {
        if (material == null)
            return;

        if (material.HasProperty("_Surface"))
            material.SetFloat("_Surface", 1f);
        if (material.HasProperty("_Blend"))
            material.SetFloat("_Blend", 0f);
        if (material.HasProperty("_SrcBlend"))
            material.SetFloat("_SrcBlend", (float)UnityEngine.Rendering.BlendMode.SrcAlpha);
        if (material.HasProperty("_DstBlend"))
            material.SetFloat("_DstBlend", (float)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
        if (material.HasProperty("_ZWrite"))
            material.SetFloat("_ZWrite", 0f);

        material.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent;
    }

    private Vector3 ResolveWorldPosition(Vector3 localOrWorld)
    {
        Vector3 pos = localOrWorld;
        if (visualizationRoot != null)
            pos = visualizationRoot.TransformPoint(pos);
        return pos + worldOffset;
    }

    private void CacheRootRenderers()
    {
        if (visualizationRoot != null)
            rootRenderers = visualizationRoot.GetComponentsInChildren<Renderer>(true);
        else
            rootRenderers = null;
    }

    private Vector3 GetSurfacePointFromBounds(Vector3 fallbackPoint, Vector3 outwardDirection)
    {
        if (rootRenderers == null || rootRenderers.Length == 0)
            return fallbackPoint;

        Bounds combined = rootRenderers[0].bounds;
        for (int i = 1; i < rootRenderers.Length; i++)
            combined.Encapsulate(rootRenderers[i].bounds);

        Vector3 dir = outwardDirection.normalized;
        if (dir.sqrMagnitude < 0.000001f)
            return fallbackPoint;

        Ray ray = new Ray(combined.center, dir);
        if (combined.IntersectRay(ray, out float distance))
            return ray.GetPoint(distance + arrowSurfaceOffset);

        return fallbackPoint;
    }
}

