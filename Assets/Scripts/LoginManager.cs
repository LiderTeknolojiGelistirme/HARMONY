using UnityEngine;
using TMPro; // TextMeshPro için
using System.Collections; // Coroutine için
using System.Collections.Generic; // Dropdown listesi için
using Michsky.MUIP; // ModalWindowManager için
using Zenject;
// using UnityEngine.SceneManagement; // Ayrı sahneler kullanıyorsanız

public class LoginManager : MonoBehaviour
{
    [Header("Giriş Alanları")]
    public TMP_InputField usernameField;
    public TMP_InputField passwordField;
    public GameObject LoginInButton;
    public GameObject SignUpButtonButton;
    public GameObject ExitButton;
    public GameObject backButton;
    public GameObject adminRegisterButton;

    [Header("Kayıt Alanları (Çırak)")]
    public TMP_InputField registerUsernameField;
    public TMP_InputField registerPasswordField;
    public TMP_InputField confirmPasswordField;

    [Header("Genel Durum")]
    public TextMeshProUGUI statusText;

    [Header("Paneller")]
    public GameObject loginPanel;
    public GameObject registerPanel;
    public GameObject adminPanel;
    public GameObject masterPanel;
    public GameObject apprenticePanel;

    [Header("Admin - Hesap Oluşturma Alanları")]
    public TMP_InputField admin_newUsernameField;
    public TMP_InputField admin_newPasswordField;
    public TMP_InputField admin_confirmPasswordField;
    public TMP_Dropdown admin_roleDropdown; // Sadece "Admin" ve "Master" içermeli
    public TextMeshProUGUI admin_statusText;

    [Header("Pop-up Mesaj")]
    public ModalWindowManager popupModalWindow;

    
    [Inject] private DatabaseManager databaseManager;

    // YENİ: Oturum (Session) Bilgileri
    private int loggedInUserID;
    private int loggedInUserRoleID;
    private bool isLoggedIn = false;

    // Status text temizleme için coroutine referansları
    private Coroutine statusTextClearCoroutine;
    private Coroutine adminStatusTextClearCoroutine;

    void Awake()
    {
        // Injection henüz yapılmamış olabilir, Start'ta tekrar kontrol edeceğiz
    }

    void Start()
    {
        // Eğer injection henüz yapılmadıysa, SceneContext'ten manuel olarak al
        if (databaseManager == null)
        {
            // SceneContext'i bul ve container'dan resolve et
            var sceneContext = FindFirstObjectByType<SceneContext>();
            if (sceneContext != null && sceneContext.Container != null)
            {
                try
                {
                    databaseManager = sceneContext.Container.Resolve<DatabaseManager>();
                    Debug.Log("LoginManager: DatabaseManager manuel olarak SceneContext'ten alındı.");
                }
                catch (System.Exception ex)
                {
                    Debug.LogError($"LoginManager: DatabaseManager SceneContext'ten alınamadı! Hata: {ex.Message}");
                }
            }
            
            // Hala null ise hata ver
            if (databaseManager == null)
            {
                Debug.LogError("LoginManager: DatabaseManager inject edilemedi! SceneContext'in Installers listesinde GameInstaller olduğundan ve GameConfig'in atandığından emin olun.");
                return;
            }
        }
        
        // Başlangıçta sadece giriş ve kayıt panelleri görünsün
        loginPanel.SetActive(true);
        registerPanel.SetActive(false); // Veya ayrı bir butona bağlayabilirsiniz
        
        adminPanel.SetActive(false);
        masterPanel.SetActive(false);
        apprenticePanel.SetActive(false);

        // Başlangıçta admin hesap oluşturma butonunu gizle
        if (adminRegisterButton != null)
        {
            adminRegisterButton.SetActive(false);
        }

        SetupAdminRoleDropdown();
        
        // ModalWindowManager'ın OK butonunu kapatma işlemine bağla
        if (popupModalWindow != null)
        {
            // closeOnConfirm false ise manuel olarak Close() metodunu ekle
            if (!popupModalWindow.closeOnConfirm)
            {
                popupModalWindow.onConfirm.AddListener(OnPopupOkButtonClicked);
            }
            
            // Cancel butonunu gizle (sadece OK butonu gösterilsin)
            if (popupModalWindow.cancelButton != null)
            {
                popupModalWindow.showCancelButton = false;
                popupModalWindow.UpdateUI();
            }
        }

        // Status text temizleme coroutine'lerini başlat
        StartStatusTextAutoClear();
    }

    /// <summary>
    /// Admin panelindeki rol dropdown'ını ayarlar.
    /// </summary>
    private void SetupAdminRoleDropdown()
    {
        // Kural 1 & 2: Admin sadece Admin veya Usta oluşturabilir.
        admin_roleDropdown.ClearOptions();
        admin_roleDropdown.AddOptions(new List<string> { "Admin", "Master" });
    }

    /// <summary>
    /// Giriş Yap butonuna tıklandığında çalışır.
    /// </summary>
    public async void OnLoginButtonClicked()
    {
        string username = usernameField.text;
        string password = passwordField.text;

        if (string.IsNullOrEmpty(username) || string.IsNullOrEmpty(password))
        {
            SetStatusText("Kullanıcı adı ve şifre boş olamaz.");
            return;
        }

        SetStatusText("Giriş yapılıyor...");

        if (databaseManager == null)
        {
            SetStatusText("Hata: Veritabanı bağlantısı kurulamadı.");
            Debug.LogError("DatabaseManager null! Zenject injection kontrol edin.");
            return;
        }

        DatabaseManager.LoginResult result = await databaseManager.ValidateLoginAsync(username, password);

        if (result.IsSuccess)
        {
            SetStatusText("Giriş başarılı!");
            
            // YENİ: Oturum bilgilerini kaydet
            isLoggedIn = true;
            loggedInUserID = result.UserID;
            loggedInUserRoleID = result.RoleID;

            // Alanları temizle
            usernameField.text = "";
            passwordField.text = "";

            // YENİ: Rol bazlı paneli göster
            ShowPostLoginUI(result.RoleID);
        }
        else
        {
            SetStatusText("Giriş başarısız: " + result.ErrorMessage);
        }
    }

    public void OnClickExit()
    {
        Application.Quit();
    }

    /// <summary>
    // Kullanıcının rolüne göre doğru paneli etkinleştirir.
    /// </summary>
    private void ShowPostLoginUI(int roleID)
    {
        // Önce tüm giriş panellerini gizle
        loginPanel.SetActive(false);
        registerPanel.SetActive(false);
        SetStatusText(""); // Ana durum yazısını temizle

        // Rol'e göre ilgili paneli aç ve admin butonunu kontrol et
        switch(roleID)
        {
            case DatabaseManager.RoleIDs.Admin:
                adminPanel.SetActive(true);
                // Admin giriş yaptığında hesap oluşturma butonunu göster
                if (adminRegisterButton != null)
                {
                    adminRegisterButton.SetActive(true);
                }
                break;
            case DatabaseManager.RoleIDs.Master:
                masterPanel.SetActive(true);
                // Usta giriş yaptığında hesap oluşturma butonunu gizle
                if (adminRegisterButton != null)
                {
                    adminRegisterButton.SetActive(false);
                }
                break;
            case DatabaseManager.RoleIDs.Apprentice:
                apprenticePanel.SetActive(true);
                // Çırak giriş yaptığında hesap oluşturma butonunu gizle
                if (adminRegisterButton != null)
                {
                    adminRegisterButton.SetActive(false);
                }
                break;
            default:
                Debug.LogError("Bilinmeyen rol ID'si: " + roleID);
                statusText.text = "Giriş başarılı ancak rol paneli bulunamadı.";
                break;
        }
    }

    /// <summary>
    /// (Kural 3 & 4) Kayıt Ol butonuna tıklandığında çalışır. Sadece 'Çırak' hesabı oluşturur.
    /// </summary>
    public async void OnRegisterButtonClicked()
    {
        string username = registerUsernameField.text;
        string password = registerPasswordField.text;
        string confirmPassword = confirmPasswordField.text;

        if (string.IsNullOrEmpty(username) || string.IsNullOrEmpty(password))
        {
            SetStatusText("Kullanıcı adı ve şifre boş olamaz.");
            return;
        }

        if (password != confirmPassword)
        {
            SetStatusText("Parolalar eşleşmiyor.");
            return;
        }

        SetStatusText("Çırak hesabı oluşturuluyor...");
        DatabaseManager.RegistrationResult result = await databaseManager.RegisterApprenticeAsync(username, password);

        if (result.IsSuccess)
        {
            registerUsernameField.text = "";
            registerPasswordField.text = "";
            confirmPasswordField.text = "";
            ShowPopupMessage("Hesap başarıyla oluşturuldu! Şimdi giriş yapabilirsiniz.");
        }
        else
        {
            SetStatusText("Kayıt başarısız: " + result.ErrorMessage);
        }
    }


    // YENİ EKLENDİ: (Kural 1 & 2) Admin panelindeki "Hesap Oluştur" butonu için
    /// <summary>
    /// Admin panelinden yeni bir Admin veya Master hesabı oluşturur.
    /// </summary>
    public async void OnAdminCreateAccountClicked()
    {
        //1. Yetki Kontrolü
        if (!isLoggedIn || loggedInUserRoleID != DatabaseManager.RoleIDs.Admin)
        {
            SetAdminStatusText("Yetki hatası: Bu işlemi sadece Admin yapabilir.");
            return;
        }

        // 2. Form Bilgilerini Al
        string newUsername = admin_newUsernameField.text;
        string newPassword = admin_newPasswordField.text;
        string confirmPassword = admin_confirmPasswordField.text;

        if (string.IsNullOrEmpty(newUsername) || string.IsNullOrEmpty(newPassword))
        {
            SetAdminStatusText("Yeni kullanıcı adı ve şifre boş olamaz.");
            return;
        }

        if (newPassword != confirmPassword)
        {
            SetAdminStatusText("Parolalar eşleşmiyor.");
            return;
        }

        // 3. Dropdown'dan Rol ID'sini Al
        int newAccountRoleID = 0;
        string selectedRoleText = admin_roleDropdown.options[admin_roleDropdown.value].text;
        
        if (selectedRoleText == "Admin")
        {
            newAccountRoleID = DatabaseManager.RoleIDs.Admin;
        }
        else if (selectedRoleText == "Master")
        {
            newAccountRoleID = DatabaseManager.RoleIDs.Master;
        }
        else
        {
            SetAdminStatusText("Geçersiz rol seçimi.");
            return;
        }

        // 4. Veritabanı İşlemi
        SetAdminStatusText($"{selectedRoleText} hesabı oluşturuluyor...");

        // 'CreateAccountByAdminAsync' metodunu çağırıyoruz.
        // 'loggedInUserID' parametresini 'performingAdminUserID' olarak iletiyoruz.
        DatabaseManager.RegistrationResult result = await databaseManager.CreateAccountByAdminAsync(
            newUsername, 
            newPassword, 
            newAccountRoleID, 
            loggedInUserID // İşlemi yapan Admin'in ID'si
        );

        // 5. Sonucu Göster
        if (result.IsSuccess)
        {
            admin_newUsernameField.text = "";
            admin_newPasswordField.text = "";
            admin_confirmPasswordField.text = "";
            ShowPopupMessage($"{selectedRoleText} hesabı başarıyla oluşturuldu.");
        }
        else
        {
            SetAdminStatusText("Hesap oluşturma başarısız: " + result.ErrorMessage);
        }
    }

    // YENİ EKLENDİ: Çıkış Yap butonu için
    /// <summary>
    /// Kullanıcı oturumunu kapatır ve giriş ekranına döner.
    /// </summary>
    public void OnLogoutButtonClicked()
    {
        // Oturum bilgilerini sıfırla
        isLoggedIn = false;
        loggedInUserID = 0;
        loggedInUserRoleID = 0;

        // Tüm oturum panellerini gizle
        adminPanel.SetActive(false);
        masterPanel.SetActive(false);
        apprenticePanel.SetActive(false);

        // Admin hesap oluşturma butonunu gizle
        if (adminRegisterButton != null)
        {
            adminRegisterButton.SetActive(false);
        }

        // Giriş/Kayıt panellerini göster
        loginPanel.SetActive(true);
        registerPanel.SetActive(true);
        SetStatusText("Çıkış yapıldı.");
    }

    /// <summary>
    /// Pop-up mesajı gösterir.
    /// </summary>
    /// <param name="message">Gösterilecek mesaj</param>
    private void ShowPopupMessage(string message)
    {
        if (popupModalWindow != null)
        {
            // Mesajı windowDescription'a yaz
            if (popupModalWindow.windowDescription != null)
            {
                popupModalWindow.windowDescription.text = message;
            }
            
            // Cancel butonunu gizle (sadece OK butonu gösterilsin)
            if (popupModalWindow.cancelButton != null)
            {
                popupModalWindow.showCancelButton = false;
            }
            
            // Confirm butonunu göster
            if (popupModalWindow.confirmButton != null)
            {
                popupModalWindow.showConfirmButton = true;
            }
            
            // UI'ı güncelle
            popupModalWindow.UpdateUI();
            
            // ModalWindowManager'ın Open metodunu kullan (SetActive yerine)
            popupModalWindow.Open();
        }
        else
        {
            Debug.LogWarning("Pop-up ModalWindowManager atanmamış!");
        }
    }

    /// <summary>
    /// Pop-up mesajını kapatır. ModalWindowManager'ın confirmButton event'ine bağlanır.
    /// </summary>
    public void OnPopupOkButtonClicked()
    {
        if (popupModalWindow != null)
        {
            popupModalWindow.Close();
        }
    }

    /// <summary>
    /// Status text'i ayarlar ve 5 saniye sonra otomatik temizler.
    /// </summary>
    private void SetStatusText(string message)
    {
        if (statusText != null)
        {
            statusText.text = message;
            
            // Önceki coroutine'i durdur
            if (statusTextClearCoroutine != null)
            {
                StopCoroutine(statusTextClearCoroutine);
                statusTextClearCoroutine = null;
            }
            
            // Boş string değilse yeni coroutine başlat
            if (!string.IsNullOrEmpty(message))
            {
                statusTextClearCoroutine = StartCoroutine(ClearStatusTextAfterDelay(5f));
            }
        }
    }

    /// <summary>
    /// Admin status text'i ayarlar ve 5 saniye sonra otomatik temizler.
    /// </summary>
    private void SetAdminStatusText(string message)
    {
        if (admin_statusText != null)
        {
            admin_statusText.text = message;
            
            // Önceki coroutine'i durdur
            if (adminStatusTextClearCoroutine != null)
            {
                StopCoroutine(adminStatusTextClearCoroutine);
                adminStatusTextClearCoroutine = null;
            }
            
            // Boş string değilse yeni coroutine başlat
            if (!string.IsNullOrEmpty(message))
            {
                adminStatusTextClearCoroutine = StartCoroutine(ClearAdminStatusTextAfterDelay(5f));
            }
        }
    }

    /// <summary>
    /// Belirtilen süre sonra status text'i temizler.
    /// </summary>
    private IEnumerator ClearStatusTextAfterDelay(float delay)
    {
        yield return new WaitForSeconds(delay);
        
        if (statusText != null)
        {
            statusText.text = "";
        }
        
        statusTextClearCoroutine = null;
    }

    /// <summary>
    /// Belirtilen süre sonra admin status text'i temizler.
    /// </summary>
    private IEnumerator ClearAdminStatusTextAfterDelay(float delay)
    {
        yield return new WaitForSeconds(delay);
        
        if (admin_statusText != null)
        {
            admin_statusText.text = "";
        }
        
        adminStatusTextClearCoroutine = null;
    }

    /// <summary>
    /// Status text otomatik temizleme sistemini başlatır.
    /// </summary>
    private void StartStatusTextAutoClear()
    {
        // Her 5 saniyede bir status text'leri kontrol et ve temizle
        StartCoroutine(AutoClearStatusTexts());
    }

    /// <summary>
    /// Her 5 saniyede bir status text'leri kontrol eder ve temizler.
    /// </summary>
    private IEnumerator AutoClearStatusTexts()
    {
        while (true)
        {
            yield return new WaitForSeconds(5f);
            
            // Eğer status text doluysa ve coroutine çalışmıyorsa temizle
            if (statusText != null && !string.IsNullOrEmpty(statusText.text) && statusTextClearCoroutine == null)
            {
                statusText.text = "";
            }
            
            if (admin_statusText != null && !string.IsNullOrEmpty(admin_statusText.text) && adminStatusTextClearCoroutine == null)
            {
                admin_statusText.text = "";
            }
        }
    }
}