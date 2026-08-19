using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using TMPro;
using RosSharp.RosBridgeClient;
using std = RosSharp.RosBridgeClient.MessageTypes.Std;
using bk = RosSharp.RosBridgeClient.MessageTypes.Backend;
using Zenject;

/// <summary>
/// Qt tabanlı "MAGICIAN PLC Control System" arayüzünün (gui_app/mainwindow.cpp) ML2
/// karşılığı. Tüm komutları ROS2 service çağrılarıyla gönderir, durumu Bool/Int16
/// topic abonelikleriyle alıp toggle butonların rengini/metnini günceller.
/// ros-sharp callback'leri ana thread'de gelmez; bu yüzden RobotStatusSubscriber.cs
/// deseni izlenir: callback yalnızca alan yazar, UI güncellemesi Update()'te yapılır.
/// </summary>
public class PlcControlPanel : MonoBehaviour
{
    /// <summary>Tek bir SetBool toggle butonu + komut servisi + durum topic'i eşlemesi.</summary>
    [Serializable]
    public class ToggleControl
    {
        [Tooltip("Buton üzerinde gösterilecek ana etiket (örn. 'Safe Transfer')")]
        public string label;

        [Tooltip("Tıklandığında basılacak buton")]
        public Button button;

        [Tooltip("Komut servisi (std_srvs/SetBool), örn. /ros2_comm/sensing/active_set")]
        public string commandService;

        [Tooltip("Durum topic'i (std_msgs/Bool), örn. /ros2_comm/sensing/sensing_active")]
        public string stateTopic;

        // --- Runtime durumu (serialize edilmez) ---
        [NonSerialized] public bool state;
        [NonSerialized] public bool hasPending;
        [NonSerialized] public bool pendingState;
        [NonSerialized] public Image image;
        [NonSerialized] public TMP_Text text;
    }

    [Header("ROS Bağlantısı")]
    [Tooltip("Zenject inject edemezse kullanılacak yedek referans (RosConnectorObject)")]
    [SerializeField] private RosConnector rosConnectorRef;
    [Inject(Optional = true)] private RosConnector injectedConnector;

    [Header("Speed Control")]
    [SerializeField] private TMP_InputField speedInput;
    [SerializeField] private Button speedSetButton;

    [Header("Slider1 Control")]
    [SerializeField] private TMP_InputField slider1Input;
    [SerializeField] private Button slider1SetButton;
    [SerializeField] private Button slider1GoButton;

    [Header("Slider2 Control")]
    [SerializeField] private TMP_InputField slider2Input;
    [SerializeField] private Button slider2SetButton;
    [SerializeField] private Button slider2GoButton;

    [Header("Toggle Kontrolleri (COBOT + Sensing + Cleaning)")]
    [Tooltip("Inspector'da her toggle için label/button/commandService/stateTopic doldur")]
    [SerializeField] private List<ToggleControl> toggles = new List<ToggleControl>();

    [Header("Durum Çubuğu")]
    [Tooltip("/ros2_comm/speed (Int16) güncel hızı gösterir")]
    [SerializeField] private TMP_Text statusText;

    // Qt renkleri (mainwindow.cpp:312-329) — ON: #27ae60, OFF: #e74c3c
    private static readonly Color OnColor = new Color(39f / 255f, 174f / 255f, 96f / 255f);
    private static readonly Color OffColor = new Color(231f / 255f, 76f / 255f, 60f / 255f);

    // Komut servis adları (Qt mainwindow.cpp setup_ros)
    private const string SvcSpeedSet = "/ros2_comm/speed_set";
    private const string SvcSlider1SetPos = "/ros2_comm/slider1/set_pos";
    private const string SvcSlider2SetPos = "/ros2_comm/slider2/set_pos";
    private const string SvcSlider1GoPos = "/ros2_comm/slider1/go_pos";
    private const string SvcSlider2GoPos = "/ros2_comm/slider2/go_pos";
    private const string TopicSpeed = "/ros2_comm/speed";

    // Speed geri-bildirimi için thread-safe alanlar
    private short latestSpeed;
    private bool hasSpeedUpdate;

    private RosConnector Ros => injectedConnector != null ? injectedConnector : rosConnectorRef;

    void Start()
    {
        if (Ros == null)
        {
            Debug.LogError("[PlcControlPanel] RosConnector yok (inject de serialize de edilmemiş)!");
            return;
        }

        if (Ros.RosSocket == null)
        {
            Debug.LogWarning("[PlcControlPanel] RosSocket henüz hazır değil; abonelikler kurulmadan dönülüyor.");
            return;
        }

        // --- Speed / Slider butonları ---
        if (speedSetButton != null)
            speedSetButton.onClick.AddListener(OnSpeedSet);
        if (slider1SetButton != null)
            slider1SetButton.onClick.AddListener(() => OnSliderSet(SvcSlider1SetPos, slider1Input));
        if (slider2SetButton != null)
            slider2SetButton.onClick.AddListener(() => OnSliderSet(SvcSlider2SetPos, slider2Input));
        if (slider1GoButton != null)
            slider1GoButton.onClick.AddListener(() => CallBool(SvcSlider1GoPos, true));
        if (slider2GoButton != null)
            slider2GoButton.onClick.AddListener(() => CallBool(SvcSlider2GoPos, true));

        Ros.RosSocket.Subscribe<std.Int16>(TopicSpeed, OnSpeedFeedback);

        // --- Toggle kontrolleri ---
        foreach (var t in toggles)
        {
            if (t.button == null)
            {
                Debug.LogWarning($"[PlcControlPanel] '{t.label}' toggle'ında buton atanmamış, atlanıyor.");
                continue;
            }

            t.image = t.button.targetGraphic as Image;
            t.text = t.button.GetComponentInChildren<TMP_Text>();
            ApplyToggleStyle(t); // başlangıç OFF görünümü

            var captured = t; // closure
            t.button.onClick.AddListener(() => OnToggleClicked(captured));

            if (!string.IsNullOrEmpty(t.stateTopic))
                Ros.RosSocket.Subscribe<std.Bool>(t.stateTopic, msg => OnToggleFeedback(captured, msg));
        }

        Debug.Log($"[PlcControlPanel] {toggles.Count} toggle + speed aboneliği kuruldu.");
    }

    void Update()
    {
        // Speed geri-bildirimi (ana thread)
        if (hasSpeedUpdate)
        {
            if (statusText != null)
                statusText.text = $"Current Speed: {latestSpeed}";
            hasSpeedUpdate = false;
        }

        // Toggle durum geri-bildirimleri
        foreach (var t in toggles)
        {
            if (t.hasPending)
            {
                t.state = t.pendingState;
                ApplyToggleStyle(t);
                t.hasPending = false;
            }
        }
    }

    // ---------------------------------------------------------------- Komutlar

    /// <summary>Speed input'undaki değeri okuyup /ros2_comm/speed_set servisine gönderir.</summary>
    private void OnSpeedSet()
    {
        if (speedInput == null) return;
        if (!short.TryParse(speedInput.text, out short value))
        {
            SetStatus("Geçersiz hız değeri");
            return;
        }
        var request = new bk.SetInt16Request(value);
        Ros.RosSocket.CallService<bk.SetInt16Request, bk.SetInt16Response>(
            SvcSpeedSet,
            r => Debug.Log($"[PlcControlPanel] speed_set yanıt: {r.message}"),
            request);
        SetStatus($"Speed set to: {value}");
    }

    /// <summary>Slider input'undaki float değeri ilgili set_pos servisine gönderir.</summary>
    private void OnSliderSet(string service, TMP_InputField input)
    {
        if (input == null) return;
        if (!float.TryParse(input.text, out float value))
        {
            SetStatus("Geçersiz slider değeri");
            return;
        }
        var request = new bk.SetFloat32Request(value);
        Ros.RosSocket.CallService<bk.SetFloat32Request, bk.SetFloat32Response>(
            service,
            r => Debug.Log($"[PlcControlPanel] {service} yanıt: {r.message}"),
            request);
        SetStatus($"Slider set to {value}");
    }

    /// <summary>Toggle butona basıldığında durumu ters çevirip SetBool servisini çağırır.</summary>
    private void OnToggleClicked(ToggleControl t)
    {
        t.state = !t.state;
        ApplyToggleStyle(t); // anında geri-bildirim (Qt updateToggleButtonStyle gibi)
        CallBool(t.commandService, t.state);
    }

    /// <summary>std_srvs/SetBool servisi çağrısı için ortak yardımcı.</summary>
    private void CallBool(string service, bool value)
    {
        if (string.IsNullOrEmpty(service)) return;
        var request = new std.SetBoolRequest(value);
        Ros.RosSocket.CallService<std.SetBoolRequest, std.SetBoolResponse>(
            service,
            r => Debug.Log($"[PlcControlPanel] {service} yanıt: success={r.success} msg={r.message}"),
            request);
    }

    // ------------------------------------------------------- Abonelik callback'leri
    // DİKKAT: bu callback'ler ana thread'de DEĞİL — yalnızca alan yaz, UI'ı Update() günceller.

    private void OnSpeedFeedback(std.Int16 message)
    {
        latestSpeed = message.data;
        hasSpeedUpdate = true;
    }

    private void OnToggleFeedback(ToggleControl t, std.Bool message)
    {
        t.pendingState = message.data;
        t.hasPending = true;
    }

    // ------------------------------------------------------------------ Görsel

    /// <summary>Toggle butonun rengini ve "LABEL: ON/OFF" metnini state'e göre ayarlar.</summary>
    private void ApplyToggleStyle(ToggleControl t)
    {
        if (t.image != null)
            t.image.color = t.state ? OnColor : OffColor;
        if (t.text != null)
            t.text.text = $"{t.label}: {(t.state ? "ON" : "OFF")}";
    }

    private void SetStatus(string msg)
    {
        if (statusText != null)
            statusText.text = msg;
    }
}
