using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.XR.Interaction.Toolkit.Inputs;

/// <summary>
/// Defect noktası ile defect paneli arasında yarı saydam bir çizgi oluşturur.
/// ML2 controller trackpad'i ile paneli 6 yönde hareket ettirmeyi sağlar.
///   - Trigger basılı + Trackpad: sol/sağ (X) ve ileri/geri (Z)
///   - Bumper basılı  + Trackpad Y: yukarı/aşağı (Y)
/// Hareket yönü kamera (head) referanslıdır.
/// </summary>
public class DefectPanelConnector : MonoBehaviour
{
    [Header("Bağlantı Objeleri")]
    [SerializeField] private GameObject defect;
    [SerializeField] private GameObject defectPanel;

    [Header("Çizgi Ayarları")]
    [SerializeField] private Color lineColor = new Color(1f, 1f, 1f, 0.4f);
    [SerializeField] private float lineWidth = 0.003f;
    [SerializeField] private int capVertices = 6;

    [Header("Render Sıralaması (Sorting)")]
    [SerializeField] private string sortingLayerName = "Default";
    [Tooltip("Defect en arkada, çizgi ortada, panel en önde render edilir")]
    [SerializeField] private int defectSortingOrder = 0;
    [SerializeField] private int lineSortingOrder = 5;
    [SerializeField] private int panelSortingOrder = 10;
    [Tooltip("Material render queue: düşük değer önce çizilir (daha arkada kalır)")]
    [SerializeField] private int lineRenderQueue = 3000;
    [SerializeField] private int panelRenderQueue = 3100;

    [Header("Bağlantı Noktası Offset")]
    [Tooltip("Çizgi defect objesinin ne kadar üstünden çıkar")]
    [SerializeField] private float defectTopOffset = 0.03f;
    [Tooltip("Çizgi panelin ne kadar arkasına bağlanır")]
    [SerializeField] private float panelBackOffset = 0.02f;

    [Header("Hareket Ayarları")]
    [Tooltip("Panelin hareket hızı (m/s)")]
    [SerializeField] private float moveSpeed = 1.5f;
    [Tooltip("Trackpad dead-zone eşiği")]
    [SerializeField] private float deadZone = 0.15f;
    [Tooltip("Hareket yönü için referans kamera (null ise Camera.main)")]
    [SerializeField] private Camera referenceCamera;

    private LineRenderer lineRenderer;
    private Material lineMaterial;

    private InputActionMap controllerMap;
    private InputAction triggerAction;
    private InputAction bumperAction;
    private InputAction trackpadAction;
    private bool inputInitialized;

    private void Awake()
    {
        if (referenceCamera == null)
            referenceCamera = Camera.main;
    }

    private void Start()
    {
        CreateLineRenderer();
        TryInitializeInput();
    }

    private void Update()
    {
        if (!inputInitialized)
            TryInitializeInput();

        UpdateLine();
        HandleMovement();
    }

    private void CreateLineRenderer()
    {
        var lineGo = new GameObject("DefectPanelLine");
        lineGo.transform.SetParent(transform, false);
        lineRenderer = lineGo.AddComponent<LineRenderer>();
        lineRenderer.positionCount = 2;
        lineRenderer.useWorldSpace = true;
        lineRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        lineRenderer.receiveShadows = false;
        lineRenderer.numCapVertices = capVertices;
        lineRenderer.widthMultiplier = lineWidth;

        var shader = Shader.Find("Sprites/Default");
        if (shader != null)
        {
            lineMaterial = new Material(shader);
            lineMaterial.color = lineColor;
            lineMaterial.renderQueue = lineRenderQueue;
            lineRenderer.material = lineMaterial;
        }

        lineRenderer.startColor = lineColor;
        lineRenderer.endColor = lineColor;

        ApplyLayerSorting();
    }

    private void ApplyLayerSorting()
    {
        if (lineRenderer != null)
        {
            lineRenderer.sortingLayerName = sortingLayerName;
            lineRenderer.sortingOrder = lineSortingOrder;
        }

        if (defect != null)
        {
            foreach (var r in defect.GetComponentsInChildren<Renderer>(true))
            {
                r.sortingLayerName = sortingLayerName;
                r.sortingOrder = defectSortingOrder;
            }

            var defectCanvas = defect.GetComponentInChildren<Canvas>(true);
            if (defectCanvas != null)
            {
                defectCanvas.overrideSorting = true;
                defectCanvas.sortingLayerName = sortingLayerName;
                defectCanvas.sortingOrder = defectSortingOrder;
            }
        }

        if (defectPanel != null)
        {
            foreach (var r in defectPanel.GetComponentsInChildren<Renderer>(true))
            {
                r.sortingLayerName = sortingLayerName;
                r.sortingOrder = panelSortingOrder;
                if (r.material != null)
                    r.material.renderQueue = panelRenderQueue;
            }

            var panelCanvas = defectPanel.GetComponentInChildren<Canvas>(true);
            if (panelCanvas != null)
            {
                panelCanvas.overrideSorting = true;
                panelCanvas.sortingLayerName = sortingLayerName;
                panelCanvas.sortingOrder = panelSortingOrder;
            }
        }
    }

    private void TryInitializeInput()
    {
        var manager = FindAnyObjectByType<InputActionManager>();
        if (manager == null || manager.actionAssets == null || manager.actionAssets.Count == 0)
            return;

        foreach (var asset in manager.actionAssets)
        {
            var map = asset.FindActionMap("Controller");
            if (map == null)
                continue;

            controllerMap = map;
            triggerAction = map.FindAction("Trigger");
            bumperAction = map.FindAction("Bumper");
            trackpadAction = map.FindAction("Trackpad");

            if (triggerAction != null && trackpadAction != null)
            {
                inputInitialized = true;
                Debug.Log("[DefectPanelConnector] Controller input başarıyla bağlandı.");
                return;
            }
        }
    }

    private void UpdateLine()
    {
        if (lineRenderer == null || defect == null || defectPanel == null)
            return;

        Vector3 defectTop = defect.transform.position + Vector3.up * defectTopOffset;
        Vector3 panelBack = defectPanel.transform.position - defectPanel.transform.forward * panelBackOffset;

        lineRenderer.SetPosition(0, defectTop);
        lineRenderer.SetPosition(1, panelBack);
    }

    private void HandleMovement()
    {
        if (!inputInitialized || defectPanel == null)
            return;

        bool triggerHeld = triggerAction != null && triggerAction.IsPressed();
        bool bumperHeld = bumperAction != null && bumperAction.IsPressed();

        if (!triggerHeld && !bumperHeld)
            return;

        Vector2 touchPos = trackpadAction != null ? trackpadAction.ReadValue<Vector2>() : Vector2.zero;

        if (touchPos.sqrMagnitude < deadZone * deadZone)
            return;

        Vector3 moveDir = Vector3.zero;

        if (referenceCamera == null)
            referenceCamera = Camera.main;

        Vector3 camForward = referenceCamera != null ? referenceCamera.transform.forward : Vector3.forward;
        Vector3 camRight = referenceCamera != null ? referenceCamera.transform.right : Vector3.right;

        camForward.y = 0f;
        camForward.Normalize();
        camRight.y = 0f;
        camRight.Normalize();

        if (bumperHeld)
        {
            moveDir += Vector3.up * touchPos.y;
        }
        else if (triggerHeld)
        {
            moveDir += camRight * touchPos.x;
            moveDir += camForward * touchPos.y;
        }

        defectPanel.transform.position += moveDir.normalized * moveSpeed * Time.deltaTime;
    }

    private void OnDestroy()
    {
        if (lineRenderer != null)
            Destroy(lineRenderer.gameObject);
        if (lineMaterial != null)
            Destroy(lineMaterial);
    }
}
