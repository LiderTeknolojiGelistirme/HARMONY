using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Açılışta çalışma kipini sorar: DEMO GUI (ROS2 olmadan, sahte veriyle) veya
/// ROS2 GUI (normal, rosbridge'e bağlı akış).
///
/// DEMO seçildiğinde <see cref="HarmonyDemoScenario"/> etkinleştirilir ve
/// komut butonları senaryoyu sürer. ROS2 seçildiğinde hiçbir şey değişmez;
/// sahne eskisi gibi davranır. Seçim yapılana kadar diyalog kullanıcının
/// önünde durur.
/// </summary>
public class HarmonyModeSelector : MonoBehaviour
{
    [Header("Diyalog")]
    [Tooltip("Seçim yapılınca kapatılacak kök nesne. Boş bırakılırsa bu nesne kullanılır.")]
    [SerializeField] private GameObject dialogRoot;

    [SerializeField] private Button demoButton;
    [SerializeField] private Button rosButton;

    [Tooltip("Bakım senaryolarını açan buton.")]
    [SerializeField] private Button maintenanceButton;

    [Tooltip("Açılacak bakım paneli. Boş bırakılırsa sahnede aranır.")]
    [SerializeField] private HarmonyMaintenancePanel maintenancePanel;

    [Header("Bağımlılıklar")]
    [Tooltip("DEMO seçilince açılacak senaryo motoru. Boş bırakılırsa sahnede aranır.")]
    [SerializeField] private HarmonyDemoScenario demoScenario;

    [Tooltip("Kip bilgisini taşıyacak görev denetleyicisi. Boş bırakılırsa sahnede aranır.")]
    [SerializeField] private HarmonyMissionController missionController;

    [Header("Yerleşim")]
    [Tooltip("Diyalog açılışta kullanıcının önüne taşınsın mı?")]
    [SerializeField] private bool placeInFrontOfUser = true;

    [Tooltip("Kullanıcıya uzaklık [m].")]
    [SerializeField] private float distance = 1.1f;

    [Tooltip("Göz hizasına göre dikey kayma [m].")]
    [SerializeField] private float verticalOffset = -0.05f;

    /// <summary>Demo kipi seçildi mi? Seçim yapılmadan önce false'tur.</summary>
    public static bool DemoMode { get; private set; }

    /// <summary>Kullanıcı bir kip seçti mi?</summary>
    public static bool ModeChosen { get; private set; }

    private void Awake()
    {
        if (dialogRoot == null) dialogRoot = gameObject;
        if (demoScenario == null) demoScenario = FindObjectOfType<HarmonyDemoScenario>(true);
        if (missionController == null) missionController = FindObjectOfType<HarmonyMissionController>();

        // Kip seçilene kadar demo akışı çalışmasın.
        if (demoScenario != null) demoScenario.enabled = false;

        DemoMode = false;
        ModeChosen = false;

        if (maintenancePanel == null) maintenancePanel = FindObjectOfType<HarmonyMaintenancePanel>(true);

        if (demoButton != null) demoButton.onClick.AddListener(ChooseDemo);
        if (rosButton != null) rosButton.onClick.AddListener(ChooseRos);
        if (maintenanceButton != null) maintenanceButton.onClick.AddListener(OpenMaintenance);
    }

    private void Start()
    {
        if (placeInFrontOfUser) PlaceInFront();
    }

    private void OnDestroy()
    {
        if (demoButton != null) demoButton.onClick.RemoveListener(ChooseDemo);
        if (rosButton != null) rosButton.onClick.RemoveListener(ChooseRos);
        if (maintenanceButton != null) maintenanceButton.onClick.RemoveListener(OpenMaintenance);
    }

    /// <summary>
    /// Bakım senaryolarını açar. Kip seçimi diyaloğu açık kalır; bakım bir
    /// çalışma kipi değil, yanında kullanılan bir başvuru panelidir.
    /// </summary>
    public void OpenMaintenance()
    {
        if (maintenancePanel == null)
        {
            Debug.LogWarning("HarmonyModeSelector: bakım paneli atanmamış.");
            return;
        }

        maintenancePanel.Open();
    }

    /// <summary>
    /// Diyaloğu kullanıcının bakış yönünde, göz hizasında konumlandırır.
    /// Panel yönelimini <see cref="HarmonyBillboardPanel"/> sürdüğü için
    /// burada yalnızca konum ayarlanır.
    /// </summary>
    private void PlaceInFront()
    {
        Camera cam = Camera.main;
        if (cam == null) return;

        Vector3 forward = cam.transform.forward;
        forward.y = 0f;
        if (forward.sqrMagnitude < 1e-4f) forward = Vector3.forward;

        transform.position = cam.transform.position
                             + forward.normalized * distance
                             + Vector3.up * verticalOffset;
    }

    /// <summary>DEMO GUI: senaryo sahte veriyle oynatılır, ROS'a komut gitmez.</summary>
    public void ChooseDemo()
    {
        DemoMode = true;
        ModeChosen = true;

        if (missionController != null) missionController.DemoMode = true;
        if (demoScenario != null) demoScenario.enabled = true;

        Debug.Log("HarmonyModeSelector: DEMO GUI seçildi — sahte veri akışı açık.");
        CloseDialog();
    }

    /// <summary>ROS2 GUI: normal akış, veriler rosbridge üzerinden gelir.</summary>
    public void ChooseRos()
    {
        DemoMode = false;
        ModeChosen = true;

        if (missionController != null) missionController.DemoMode = false;
        if (demoScenario != null) demoScenario.enabled = false;

        Debug.Log("HarmonyModeSelector: ROS2 GUI seçildi — canlı veri bekleniyor.");
        CloseDialog();
    }

    private void CloseDialog()
    {
        if (dialogRoot != null) dialogRoot.SetActive(false);
    }
}
