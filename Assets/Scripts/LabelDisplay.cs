using UnityEngine;
using TMPro;
using DG.Tweening;

/// <summary>
/// Bu script hedef GameObject'e eklenir.
/// Start'ta label prefab'ını instantiate eder, label üzerinde hedef objenin adını gösterir,
/// spawn animasyonu oynatır ve her repeatInterval saniyede bir animasyonu tekrar eder.
/// </summary>
public class LabelDisplay : MonoBehaviour
{
    [Header("Label Settings")]
    [SerializeField] private GameObject labelPrefab;
    [SerializeField] private float labelOffsetY = 0.15f;
    [SerializeField] private string overrideName = "";
    [SerializeField] private Camera targetCamera;

    [Header("Ownership Line")]
    [SerializeField] private GameObject dotPrefab;
    [SerializeField] private float dotScale = 0.01f;
    [SerializeField] private Vector3 dotLocalOffset = Vector3.zero;
    [SerializeField] private Material lineMaterial;
    [SerializeField] private Color lineColor = Color.white;
    [SerializeField] private float lineWidth = 0.0025f;
    [SerializeField] private Transform labelLineAnchorOverride;
    [SerializeField] private float lineEndGap = 0.02f;
    [SerializeField] private string sortingLayerName = "Default";
    [SerializeField] private int labelSortingOrder = 10;
    [SerializeField] private int lineSortingOrderOffset = -1;

    [Header("Spawn Animation")]
    [SerializeField] private float spawnStartScaleMultiplier = 0.2f;
    [SerializeField] private float spawnScaleDuration = 0.22f;
    [SerializeField] private Ease spawnEase = Ease.OutCubic;

    [Header("Jiggle Animation")]
    [SerializeField] private float jiggleStrength = 0.08f;
    [SerializeField] private float jiggleDuration = 0.25f;
    [SerializeField] private int jiggleVibrato = 10;
    [SerializeField] private float jiggleElasticity = 0.8f;

    [Header("Repeat")]
    [SerializeField] private float repeatInterval = 5f;

    private GameObject activeLabel;
    private Vector3 labelDefaultScale;
    private Sequence spawnSequence;
    private float repeatTimer;
    private Transform dotTransform;
    private LineRenderer lineRenderer;
    private Renderer labelRenderer;
    private RectTransform labelRectTransform;
    private LabelLineAnchor labelLineAnchor;
    private Canvas labelCanvas;
    private Renderer dotRenderer;

    private void Start()
    {
        if (targetCamera == null)
            targetCamera = Camera.main;

        if (labelPrefab == null)
        {
            Debug.LogError($"{nameof(LabelDisplay)} on {gameObject.name} needs a labelPrefab.", this);
            enabled = false;
            return;
        }

        activeLabel = Instantiate(labelPrefab);
        labelDefaultScale = activeLabel.transform.localScale;
        labelRenderer = activeLabel.GetComponentInChildren<Renderer>();
        labelRectTransform = activeLabel.GetComponentInChildren<RectTransform>();
        labelLineAnchor = activeLabel.GetComponentInChildren<LabelLineAnchor>(true);
        labelCanvas = activeLabel.GetComponentInChildren<Canvas>(true);

        EnsureDotAndLine();
        ApplySorting();

        SetLabelText(string.IsNullOrEmpty(overrideName) ? gameObject.name : overrideName);
        UpdateLabelTransform();

        PlaySpawnAnimation();
    }

    private void Update()
    {
        if (activeLabel == null)
            return;

        UpdateLabelTransform();
        UpdateDotAndLine();

        repeatTimer += Time.deltaTime;
        if (repeatTimer >= repeatInterval)
        {
            repeatTimer = 0f;
            PlaySpawnAnimation();
        }
    }

    private void SetLabelText(string labelText)
    {
        if (activeLabel == null)
            return;

        var tmp = activeLabel.GetComponentInChildren<TextMeshProUGUI>();
        if (tmp != null)
            tmp.text = labelText;

        var tmpWorld = activeLabel.GetComponentInChildren<TextMeshPro>();
        if (tmpWorld != null)
            tmpWorld.text = labelText;
    }

    private void UpdateLabelTransform()
    {
        if (activeLabel == null)
            return;

        Vector3 labelPos = transform.position + Vector3.up * labelOffsetY;
        activeLabel.transform.position = labelPos;

        if (targetCamera != null)
        {
            activeLabel.transform.LookAt(targetCamera.transform);
            activeLabel.transform.Rotate(0, 180, 0);
        }
    }

    private void EnsureDotAndLine()
    {
        if (dotTransform == null)
        {
            GameObject dotGo;
            if (dotPrefab != null)
            {
                dotGo = Instantiate(dotPrefab);
            }
            else
            {
                dotGo = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                dotGo.name = "OwnershipDot";
                var col = dotGo.GetComponent<Collider>();
                if (col != null)
                    Destroy(col);
            }

            dotTransform = dotGo.transform;
            dotTransform.localScale = Vector3.one * dotScale;
            dotRenderer = dotGo.GetComponent<Renderer>();
        }

        if (lineRenderer == null)
        {
            var lineGo = new GameObject("OwnershipLine");
            lineRenderer = lineGo.AddComponent<LineRenderer>();
            lineRenderer.positionCount = 2;
            lineRenderer.useWorldSpace = true;
            lineRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            lineRenderer.receiveShadows = false;
            lineRenderer.numCapVertices = 6;
            lineRenderer.widthMultiplier = lineWidth;

            if (lineMaterial == null)
            {
                var shader = Shader.Find("Sprites/Default");
                if (shader != null)
                    lineMaterial = new Material(shader);
            }

            if (lineMaterial != null)
                lineRenderer.material = lineMaterial;

            lineRenderer.startColor = lineColor;
            lineRenderer.endColor = lineColor;
        }

        if (dotTransform != null)
            dotTransform.gameObject.SetActive(true);
        if (lineRenderer != null)
            lineRenderer.gameObject.SetActive(true);

        UpdateDotAndLine();
    }

    private void ApplySorting()
    {
        int lineSortingOrder = labelSortingOrder + lineSortingOrderOffset;

        if (labelCanvas != null)
        {
            // World-space canvas/prefab'larda en sağlam yöntem
            labelCanvas.overrideSorting = true;
            labelCanvas.sortingLayerName = sortingLayerName;
            labelCanvas.sortingOrder = labelSortingOrder;
        }
        else if (labelRenderer != null)
        {
            labelRenderer.sortingLayerName = sortingLayerName;
            labelRenderer.sortingOrder = labelSortingOrder;
        }

        if (lineRenderer != null)
        {
            lineRenderer.sortingLayerName = sortingLayerName;
            lineRenderer.sortingOrder = lineSortingOrder;
        }

        if (dotRenderer != null)
        {
            dotRenderer.sortingLayerName = sortingLayerName;
            dotRenderer.sortingOrder = lineSortingOrder;
        }
    }

    private void UpdateDotAndLine()
    {
        if (dotTransform == null || lineRenderer == null || activeLabel == null)
            return;

        Vector3 dotPos = transform.TransformPoint(dotLocalOffset);
        dotTransform.position = dotPos;

        Vector3 labelCenter = GetLabelCenterWorld();
        Vector3 labelEnd = GetLineEndPoint(labelCenter);

        lineRenderer.widthMultiplier = lineWidth;
        lineRenderer.startColor = lineColor;
        lineRenderer.endColor = lineColor;
        lineRenderer.SetPosition(0, dotPos);
        lineRenderer.SetPosition(1, labelEnd);
    }

    private Vector3 GetLineEndPoint(Vector3 labelAnchorWorld)
    {
        if (lineEndGap <= 0f || targetCamera == null)
            return labelAnchorWorld;

        Vector3 camPos = targetCamera.transform.position;
        Vector3 toAnchor = labelAnchorWorld - camPos;
        float sqrMag = toAnchor.sqrMagnitude;
        if (sqrMag < 0.000001f)
            return labelAnchorWorld;

        // Label yüzeyine tam değmek yerine, kameraya doğru küçük bir boşluk bırak.
        return labelAnchorWorld - toAnchor.normalized * lineEndGap;
    }

    private Vector3 GetLabelCenterWorld()
    {
        if (labelLineAnchorOverride != null)
            return labelLineAnchorOverride.position;

        if (labelLineAnchor != null && labelLineAnchor.Anchor != null)
            return labelLineAnchor.Anchor.position;

        if (labelRectTransform != null)
        {
            Vector3[] corners = new Vector3[4];
            labelRectTransform.GetWorldCorners(corners);
            return (corners[0] + corners[1] + corners[2] + corners[3]) * 0.25f;
        }

        if (labelRenderer != null)
            return labelRenderer.bounds.center;
        return activeLabel.transform.position;
    }

    private void PlaySpawnAnimation()
    {
        if (activeLabel == null)
            return;

        spawnSequence?.Kill(complete: true);

        Transform labelTransform = activeLabel.transform;
        Vector3 startScale = labelDefaultScale * spawnStartScaleMultiplier;
        Vector3 punch = labelDefaultScale * jiggleStrength;

        labelTransform.localScale = startScale;

        spawnSequence = DOTween.Sequence();
        spawnSequence.Append(labelTransform.DOScale(labelDefaultScale, spawnScaleDuration).SetEase(spawnEase));
        spawnSequence.Append(labelTransform.DOPunchScale(punch, jiggleDuration, jiggleVibrato, jiggleElasticity));
    }

    private void OnDisable()
    {
        spawnSequence?.Kill(complete: true);
        repeatTimer = 0f;

        if (activeLabel != null)
        {
            activeLabel.SetActive(false);
            activeLabel.transform.localScale = labelDefaultScale;
        }

        if (dotTransform != null)
            dotTransform.gameObject.SetActive(false);
        if (lineRenderer != null)
            lineRenderer.gameObject.SetActive(false);
    }

    private void OnEnable()
    {
        repeatTimer = 0f;

        if (activeLabel != null)
            activeLabel.SetActive(true);
        if (dotTransform != null)
            dotTransform.gameObject.SetActive(true);
        if (lineRenderer != null)
            lineRenderer.gameObject.SetActive(true);

        ApplySorting();
    }

    private void OnDestroy()
    {
        spawnSequence?.Kill(complete: true);
        if (dotTransform != null)
            Destroy(dotTransform.gameObject);
        if (lineRenderer != null)
            Destroy(lineRenderer.gameObject);
    }
}
