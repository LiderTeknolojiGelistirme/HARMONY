using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Doosan bakım senaryolarını adım adım gösteren panel.
///
/// İki görünümü vardır: senaryo listesi ve adım anlatımı. Bir senaryo
/// seçildiğinde adımlar tek tek ilerletilir; son adımda ileri butonu FINISH
/// olur ve listeye dönülür.
///
/// Metinler Doosan'ın resmi kılavuzlarından çıkarıldı; her senaryonun altında
/// kaynak kılavuz ve sayfa numarası yazar. Uydurma adım yoktur — kılavuzda
/// karşılığı olmayan bir şey eklenmedi.
///
/// Varsayılan içerik kodda gömülüdür; Inspector'daki liste doldurulursa o
/// kullanılır, böylece metinler kod değiştirmeden düzenlenebilir.
/// </summary>
public class HarmonyMaintenancePanel : MonoBehaviour
{
    /// <summary>Senaryonun tek bir adımı.</summary>
    [Serializable]
    public class Step
    {
        [Tooltip("Adım başlığı.")]
        public string title;

        [Tooltip("Adım açıklaması.")]
        [TextArea(2, 8)]
        public string body;

        /// <summary>
        /// Yeni bir adım oluşturur.
        /// </summary>
        /// <param name="title">Adım başlığı.</param>
        /// <param name="body">Açıklama metni.</param>
        public Step(string title, string body)
        {
            this.title = title;
            this.body = body;
        }
    }

    /// <summary>Bir bakım senaryosu.</summary>
    [Serializable]
    public class Scenario
    {
        [Tooltip("Listede ve başlıkta görünen ad.")]
        public string title;

        [Tooltip("Listedeki butonun altında görünen kısa açıklama.")]
        public string summary;

        [Tooltip("Kaynak kılavuz ve sayfa bilgisi.")]
        public string source;

        [Tooltip("Sırayla gösterilecek adımlar.")]
        public List<Step> steps = new List<Step>();
    }

    [Header("İçerik")]
    [Tooltip("Boş bırakılırsa koddaki varsayılan senaryolar kullanılır.")]
    [SerializeField] private List<Scenario> scenarios = new List<Scenario>();

    [Header("Görünümler")]
    [Tooltip("Senaryo listesi görünümünün kök nesnesi.")]
    [SerializeField] private GameObject listView;

    [Tooltip("Adım anlatımı görünümünün kök nesnesi.")]
    [SerializeField] private GameObject stepView;

    [Header("Liste Görünümü")]
    [Tooltip("Senaryo butonları; senaryo sayısı kadar olmalı.")]
    [SerializeField] private Button[] scenarioButtons;

    [Tooltip("Butonların başlık metinleri, scenarioButtons ile aynı sırada.")]
    [SerializeField] private TMP_Text[] scenarioTitles;

    [Tooltip("Butonların açıklama metinleri, scenarioButtons ile aynı sırada.")]
    [SerializeField] private TMP_Text[] scenarioSummaries;

    [Header("Adım Görünümü")]
    [SerializeField] private TMP_Text headerText;
    [SerializeField] private TMP_Text counterText;
    [SerializeField] private TMP_Text stepTitleText;
    [SerializeField] private TMP_Text stepBodyText;
    [SerializeField] private TMP_Text sourceText;

    [Tooltip("İlerleme çubuğunun dolan kısmı.")]
    [SerializeField] private RectTransform progressFill;

    [Header("Açılış Yerleşimi")]
    [Tooltip("Panel açıldığında kullanıcının önüne taşınsın mı?")]
    [SerializeField] private bool placeInFrontOnOpen = true;

    [Tooltip("Kullanıcıya uzaklık [m].")]
    [SerializeField] private float openDistance = 1.15f;

    [Tooltip("Yana kayma [m]. Pozitif değer sağa; kip diyaloğunun üstüne binmesin diye.")]
    [SerializeField] private float openLateralOffset = 0.62f;

    [Header("Butonlar")]
    [SerializeField] private Button nextButton;
    [SerializeField] private TMP_Text nextLabel;
    [SerializeField] private Button backButton;
    [SerializeField] private Button closeButton;

    // İlerleme çubuğunun boş haldeki tam genişliği.
    private float progressFullWidth = -1f;

    private int activeScenario = -1;
    private int activeStep;

    private void Awake()
    {
        if (scenarios == null || scenarios.Count == 0)
            scenarios = BuildDefaultScenarios();

        if (progressFill != null) progressFullWidth = progressFill.rect.width;

        WireButtons();
        ShowList();
    }

    /// <summary>Buton olaylarını bağlar.</summary>
    private void WireButtons()
    {
        if (scenarioButtons != null)
        {
            for (int i = 0; i < scenarioButtons.Length; i++)
            {
                if (scenarioButtons[i] == null) continue;
                int index = i;   // döngü değişkenini yakala
                scenarioButtons[i].onClick.AddListener(() => StartScenario(index));
            }
        }

        if (nextButton != null) nextButton.onClick.AddListener(Next);
        if (backButton != null) backButton.onClick.AddListener(Back);
        if (closeButton != null) closeButton.onClick.AddListener(ShowList);
    }

    // ---------------------------------------------------------------------
    // Görünüm geçişleri
    // ---------------------------------------------------------------------

    /// <summary>Paneli açar ve senaryo listesini gösterir.</summary>
    public void Open()
    {
        gameObject.SetActive(true);

        if (placeInFrontOnOpen) PlaceInFront();

        ShowList();
    }

    /// <summary>
    /// Paneli kullanıcının önüne, yana kaydırarak yerleştirir. Yönelimi
    /// <see cref="HarmonyBillboardPanel"/> sürdüğü için burada yalnızca konum
    /// ayarlanır.
    /// </summary>
    private void PlaceInFront()
    {
        Camera cam = Camera.main;
        if (cam == null) return;

        Vector3 forward = cam.transform.forward;
        forward.y = 0f;
        if (forward.sqrMagnitude < 1e-4f) forward = Vector3.forward;
        forward.Normalize();

        // Sol el kuralı: Cross(up, forward) sağ vektörü verir.
        Vector3 right = Vector3.Cross(Vector3.up, forward).normalized;

        transform.position = cam.transform.position
                             + forward * openDistance
                             + right * openLateralOffset;
    }

    /// <summary>Senaryo listesine döner.</summary>
    public void ShowList()
    {
        activeScenario = -1;

        if (listView != null) listView.SetActive(true);
        if (stepView != null) stepView.SetActive(false);

        RefreshList();
    }

    /// <summary>
    /// Seçilen senaryonun ilk adımını gösterir.
    /// </summary>
    /// <param name="index">Senaryo sırası.</param>
    public void StartScenario(int index)
    {
        if (index < 0 || index >= scenarios.Count) return;

        activeScenario = index;
        activeStep = 0;

        if (listView != null) listView.SetActive(false);
        if (stepView != null) stepView.SetActive(true);

        RefreshStep();
    }

    /// <summary>Sonraki adıma geçer; son adımdaysa listeye döner.</summary>
    public void Next()
    {
        if (activeScenario < 0) return;

        var steps = scenarios[activeScenario].steps;

        if (activeStep >= steps.Count - 1)
        {
            ShowList();
            return;
        }

        activeStep++;
        RefreshStep();
    }

    /// <summary>Önceki adıma döner; ilk adımdaysa listeye döner.</summary>
    public void Back()
    {
        if (activeScenario < 0) return;

        if (activeStep == 0)
        {
            ShowList();
            return;
        }

        activeStep--;
        RefreshStep();
    }

    // ---------------------------------------------------------------------
    // Çizim
    // ---------------------------------------------------------------------

    /// <summary>Senaryo butonlarının etiketlerini doldurur.</summary>
    private void RefreshList()
    {
        if (scenarioButtons == null) return;

        for (int i = 0; i < scenarioButtons.Length; i++)
        {
            bool used = i < scenarios.Count;

            if (scenarioButtons[i] != null)
                scenarioButtons[i].gameObject.SetActive(used);

            if (!used) continue;

            if (scenarioTitles != null && i < scenarioTitles.Length && scenarioTitles[i] != null)
                scenarioTitles[i].text = scenarios[i].title;

            if (scenarioSummaries != null && i < scenarioSummaries.Length && scenarioSummaries[i] != null)
                scenarioSummaries[i].text = scenarios[i].summary;
        }
    }

    /// <summary>Güncel adımı yazar ve ilerleme çubuğunu günceller.</summary>
    private void RefreshStep()
    {
        Scenario scenario = scenarios[activeScenario];
        Step step = scenario.steps[activeStep];

        int total = scenario.steps.Count;
        bool last = activeStep >= total - 1;

        if (headerText != null) headerText.text = scenario.title;
        if (counterText != null) counterText.text = "STEP " + (activeStep + 1) + " / " + total;
        if (stepTitleText != null) stepTitleText.text = step.title;
        if (stepBodyText != null) stepBodyText.text = step.body;
        if (sourceText != null) sourceText.text = scenario.source;

        if (nextLabel != null) nextLabel.text = last ? "FINISH" : "NEXT  ›";

        if (progressFill != null && progressFullWidth > 0f)
        {
            float ratio = (activeStep + 1) / (float)total;
            var size = progressFill.sizeDelta;
            progressFill.sizeDelta = new Vector2(progressFullWidth * ratio, size.y);
        }
    }

    // ---------------------------------------------------------------------
    // Varsayılan içerik — Doosan kılavuzlarından
    // ---------------------------------------------------------------------

    /// <summary>
    /// Kılavuzlardan çıkarılan üç bakım senaryosunu kurar.
    ///
    /// Kaynaklar:
    ///   IM = Doosan Robotics Installation Manual V2.8 (doc v2.3)
    ///   UM = Doosan Robotics User Manual V3.2.0 (doc v3.0.1, MH-Series)
    /// </summary>
    private static List<Scenario> BuildDefaultScenarios()
    {
        var list = new List<Scenario>();

        // -----------------------------------------------------------------
        var tool = new Scenario
        {
            title = "Tool Replacement & I/O Verification",
            summary = "Swap the end effector on the tool flange, reconnect flange I/O and verify the signals.",
            source = "Source: User Manual V3.2.0 §3.3 (p.85-89) · Installation Manual v2.3 §3.3.2 (p.52), §8 (p.106)"
        };
        tool.steps.Add(new Step("Servo Off",
            "Press <b>Servo Off</b> on the teach pendant to cut power to the robot joints and stop the robot.\n\n" +
            "Servo On is the state where the joints are powered and the pose can be changed — it must be off before touching the tool."));
        tool.steps.Add(new Step("Shut the system down",
            "Press the shutdown button on the teach pendant, or hold the power button (upper left) for <b>2 seconds</b>. " +
            "Confirm with <b>OK</b> on the shutdown popup.\n\n" +
            "<color=#f59e0b>Caution:</color> holding the power button longer than 4 seconds forces a shutdown and may damage the robot and controller."));
        tool.steps.Add(new Step("Isolate power",
            "Disconnect the controller power cable and make sure no other power source feeds the manipulator or controller.\n\n" +
            "Do not reconnect power at any point during the work. Observe ESD rules if any part has to be opened."));
        tool.steps.Add(new Step("Remove the old tool",
            "Undo the four M6 bolts on the tool flange and take the tool off together with its bracket.\n\n" +
            "<color=#f59e0b>Caution:</color> make sure the workpiece cannot fall from the tool once power is removed — a gripper may open when it loses power."));
        tool.steps.Add(new Step("Mount the new tool",
            "Secure the tool to the flange with <b>four M6 bolts</b> at a tightening torque of <b>9 Nm</b>.\n\n" +
            "Use a <b>6 place marker pin</b> so the tool sits in a repeatable position. The flange is ISO 9409-1-50-4-M6.\n\n" +
            "Fixing methods differ per tool — also follow the tool manufacturer's manual."));
        tool.steps.Add(new Step("Connect flange I/O",
            "Connect the tool cables to the flange I/O connectors and check the pin map before powering up.\n\n" +
            "<color=#f59e0b>Note:</color> the fifth terminal of each connector outputs <b>24 V continuously</b> whenever the robot is powered."));
        tool.steps.Add(new Step("Power up and verify grounding",
            "Check the ground connection first, then reconnect power and hold the power button until the teach pendant screen comes up.\n\n" +
            "The ground conductor must satisfy the maximum current rating of the system."));
        tool.steps.Add(new Step("Test the I/O",
            "Open <b>Status &gt; I/O Overview</b> or <b>I/O Test</b> on the teach pendant and exercise the tool.\n\n" +
            "Check controller digital input/output and analog input/output as well as the flange signals."));
        tool.steps.Add(new Step("Close out the job",
            "Restore the safety functions and confirm the software safety settings are unchanged.\n\n" +
            "Perform a <b>risk assessment</b> to confirm the cell still meets the required safety level, and record the work in the robot's repair history."));
        list.Add(tool);

        // -----------------------------------------------------------------
        var recovery = new Scenario
        {
            title = "Safety Limit Recovery",
            summary = "Bring the robot back to its normal working range after a safety violation that blocks Servo On.",
            source = "Source: User Manual V3.2.0 §5.6 (p.195-197), §5.5 (p.193-194) · Installation Manual v2.3 §8 (p.106)"
        };
        recovery.steps.Add(new Step("Confirm the symptom",
            "The robot is stopped and <b>Servo On or Jog cannot be engaged</b>, even after clearing the stop.\n\n" +
            "Typical causes: the robot left its operating area, entered a protected zone, or is pressed against a fixed object so force is applied continuously."));
        recovery.steps.Add(new Step("Secure the area",
            "Clear people from the working area before moving anything. Recovery moves the robot outside its normal limits.\n\n" +
            "If the protective stop input cannot be cleared at the device, it can be released by cancelling that input in the <b>Safety I/O</b> settings."));
        recovery.steps.Add(new Step("Open the Recovery module",
            "Tap <b>Backdrive &amp; Recovery</b> at the bottom of the main menu, then <b>Software Recovery</b>.\n\n" +
            "If the servo is still on, a popup appears and the servo is turned off automatically."));
        recovery.steps.Add(new Step("Enable the servo for recovery",
            "Tap <b>Servo On to Start Recovery</b>. This powers the joints specifically for the recovery move.\n\n" +
            "The 3D simulation on the left previews the pose you are about to command."));
        recovery.steps.Add(new Step("Move back into range",
            "Use the joint buttons on the right of the Software Recovery screen, or the cockpit buttons for direct teaching, to bring the robot back inside its limits.\n\n" +
            "Changes appear in the simulation window in real time. Close the window with the X at the top left when done."));
        recovery.steps.Add(new Step("If Software Recovery is refused",
            "<color=#f59e0b>Software Recovery is not available when a joint angle limit is exceeded by more than 3 degrees.</color>\n\n" +
            "In that case use <b>Backdrive</b> (unpowered operation): it releases the joint brakes with motor power cut so the joint can be moved by hand."));
        recovery.steps.Add(new Step("Backdrive procedure",
            "1. Status &gt; <b>Backdrive</b>. If the button is greyed out, press and release the emergency stop or press Servo Off.\n" +
            "2. Tap <b>Start Backdrive Mode</b>.\n" +
            "3. Set the brake of the joint you need to move to <b>OFF (Release)</b> and move it by hand.\n" +
            "4. Set the brake back to <b>ON (Hold)</b> when done.\n\n" +
            "Move joints back one at a time, in order."));
        recovery.steps.Add(new Step("Reboot and verify",
            "Backdrive requires a restart: shut the operating program down from Power on the main menu, hold the teach pendant power button to power off, then power on again.\n\n" +
            "<color=#f59e0b>Note:</color> joints may sag temporarily in Backdrive depending on the axis position. If a joint moves faster than a set speed, all brakes engage automatically."));
        recovery.steps.Add(new Step("Close out the job",
            "Resume the safety functions and confirm the safety settings were not changed during recovery.\n\n" +
            "Perform a <b>risk assessment</b> and record the event and the corrective action in the robot's history."));
        list.Add(recovery);

        // -----------------------------------------------------------------
        var software = new Scenario
        {
            title = "Software Backup, Update & Restore",
            summary = "Record the current version, back the data up, update the robot software and roll back if needed.",
            source = "Source: User Manual V3.2.0 §5.13.6 (p.306-307) · Installation Manual v2.3 §3.4 (p.60-62), §8 (p.106)"
        };
        software.steps.Add(new Step("Record the current version",
            "Open the robot update screen and note <b>Current Version Info</b> — the application, system and OS versions.\n\n" +
            "You need this to know what you are rolling back to if the update misbehaves."));
        software.steps.Add(new Step("Back up the data",
            "Back up the teach pendant data before changing anything: database, task files and Workcell items.\n\n" +
            "A factory reset or a failed update deletes these; the backup is the only way back."));
        software.steps.Add(new Step("Bring the robot to a safe state",
            "Stop any running task and set the robot to <b>Servo Off</b>. No one should be inside the working area.\n\n" +
            "Do not update while a task is running or while the robot is holding a workpiece."));
        software.steps.Add(new Step("Enter the safety password",
            "The update and restore functions are behind the <b>Safety Password</b>. Enter it to unlock them.\n\n" +
            "Only personnel authorised for safety configuration should perform this step."));
        software.steps.Add(new Step("Run the update",
            "Select the update file from the <b>Update</b> section and start the update. Files can be fetched automatically or supplied manually from a PC or external storage.\n\n" +
            "Do not cut power during the update."));
        software.steps.Add(new Step("Verify after the update",
            "Re-open Current Version Info and confirm the application, system and OS versions are the ones you intended.\n\n" +
            "Each successful update also adds an entry to the restore list, so this version becomes a rollback point later."));
        software.steps.Add(new Step("Roll back if needed",
            "If the new version misbehaves, open <b>System restore</b>, enter the safety password, pick a version from the <b>Backup List</b> and tap <b>Restore</b>.\n\n" +
            "The list only contains versions created by the update function."));
        software.steps.Add(new Step("If the robot will not boot",
            "If a software error is detected while booting, the system enters <b>Application Recovery Mode</b>, which offers functions to preserve and restore application data.\n\n" +
            "A <b>Series Compatibility Error</b> screen instead means the controller was last used with a different robot series — that needs the robot series replacement procedure, not a restore."));
        software.steps.Add(new Step("Close out the job",
            "Confirm the safety configuration survived the update and re-verify the safety functions before returning the cell to production.\n\n" +
            "Perform a <b>risk assessment</b> and record the version change in the robot's technical documentation."));
        list.Add(software);

        return list;
    }
}
