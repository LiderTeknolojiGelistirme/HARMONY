using System;
using System.Data;
using System.IO;
using System.Threading.Tasks;
using MySqlConnector;
using UnityEngine;
using IniParser;
using IniParser.Model;

// YENİ EKLENDİ: Argon2 için gerekli kütüphaneler
using Konscious.Security.Cryptography;
using System.Security.Cryptography;
using System.Text;

// KALDIRILDI: using BCrypt.Net;

/// <summary>
/// Kullanıcı giriş (login) ve kayıt (register) işlemlerini yönetmek için veritabanı bağlantısını yönetir.
/// Parola güvenliği için Argon2id algoritmasını kullanır.
/// </summary>
public class DatabaseManager
{
    // Connection string to the MySQL database
    private string _connectionString;

    // --- YENİ EKLENDİ: Argon2 Parametreleri ve Yardımcıları ---

    // Argon2 için kullanılacak Salt (tuz) boyutu (16 byte = 128 bit)
    private const int SaltSize = 16;
    // Argon2 çıktısı olan hash boyutu (32 byte = 256 bit)
    private const int HashSize = 32;

    // Argon2id için güvenlik parametreleri (Bu ayarları sunucu gücünüze göre ayarlayabilirsiniz)
    private const int ArgonDegreeOfParallelism = 8; // Kullanılacak çekirdek sayısı
    private const int ArgonIterations = 4;           // Tekrarlama sayısı
    private const int ArgonMemorySize = 1024 * 128;  // Kullanılacak hafıza (128 MB)

    public static class RoleIDs
    {
        public const int Admin = 1;
        public const int Master = 2; // Usta
        public const int Apprentice = 3; // Çırak
    }

    /// <summary>
    /// Constructor that initializes the connection string.
    /// </summary>
    public DatabaseManager()
    {
        LoadConnectionStringFromConfig();
    }
    
    private void LoadConnectionStringFromConfig()
    {
        // ... (Bu fonksiyon değişmedi, öncekiyle aynı) ...
        string configPath = Path.Combine(Application.streamingAssetsPath, "config.ini");
        if (!File.Exists(configPath))
        {
            Debug.LogError($"Config dosyası bulunamadı! Yol: {configPath}");
            return;
        }
        try
        {
            var parser = new FileIniDataParser();
            IniData data = parser.ReadFile(configPath);
            string server = data["Database"]["Server"];
            string port = data["Database"]["Port"];
            string uid = data["Database"]["User"];
            string pwd = data["Database"]["Password"];
            string db = data["Database"]["DatabaseName"];
            if (string.IsNullOrEmpty(server) || string.IsNullOrEmpty(port))
            {
                Debug.LogError("Config dosyasında Server veya Port bilgisi eksik.");
                return;
            }
            // Kimlik bilgileri config.ini'den okunur; koda gömülmez.
            _connectionString = $"server={server};port={port};uid={uid};pwd={pwd};database={db};SslMode=None;";
            Debug.Log("Veritabanı bağlantı bilgisi config.ini dosyasından başarıyla yüklendi.");
        }
        catch (Exception e)
        {
            Debug.LogError($"Config dosyasını okurken/parse ederken bir hata oluştu: {e.Message}");
        }
    }

    // --- Sonuç Sınıfları (Değişmedi) ---
    public class LoginResult
    {
        public bool IsSuccess { get; set; }
        public int UserID { get; set; }
        public int RoleID { get; set; }
        public string ErrorMessage { get; set; }
    }

    public class RegistrationResult
    {
        public bool IsSuccess { get; set; }
        public string ErrorMessage { get; set; }
    }

    // --- GÜNCELLENMİŞ GİRİŞ METODU (ValidateLoginAsync) ---

    /// <summary>
    /// Kullanıcı giriş bilgilerini veritabanındaki Argon2id hash'i ile doğrular.
    /// </summary>
    public async Task<LoginResult> ValidateLoginAsync(string usernameInput, string passwordInput)
    {
        var result = new LoginResult { IsSuccess = false };

        try
        {
            using (var connection = new MySqlConnection(_connectionString))
            {
                await connection.OpenAsync();
                
                string query = "SELECT UserID, Role, UserPassword FROM Users WHERE UserName = @Username";
                
                using (var command = new MySqlCommand(query, connection))
                {
                    command.Parameters.AddWithValue("@Username", usernameInput);

                    using (var reader = await command.ExecuteReaderAsync())
                    {
                        if (await reader.ReadAsync())
                        {
                            string storedHashString = reader.GetString("UserPassword");
                            
                            // GÜNCELLENDİ: Argon2 ile doğrulama yap
                            if (VerifyPasswordWithArgon2(passwordInput, storedHashString))
                            {
                                // Parola doğru
                                result.IsSuccess = true;
                                result.UserID = reader.GetInt32("UserID");
                                result.RoleID = reader.GetInt32("Role");
                            }
                            else
                            {
                                // Parola yanlış
                                result.ErrorMessage = "Kullanıcı adı veya şifre hatalı.";
                            }
                        }
                        else
                        {
                            // Kullanıcı bulunamadı
                            result.ErrorMessage = "Kullanıcı adı veya şifre hatalı.";
                        }
                    }
                }
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Bir hata oluştu: {ex.Message}");
            result.ErrorMessage = "Veritabanı bağlantı hatası: " + ex.Message;
        }

        return result;
    }

    // --- YENİ KAYIT METOTLARI (Argon2 Kullanacak Şekilde Güncellendi) ---

    /// <summary>
    /// (Kural 3 & 4) 'Çırak' hesabı oluşturan metot.
    /// </summary>
    public async Task<RegistrationResult> RegisterApprenticeAsync(string usernameInput, string passwordInput)
    {
        var result = new RegistrationResult { IsSuccess = false };

        if (string.IsNullOrEmpty(usernameInput) || string.IsNullOrEmpty(passwordInput))
        {
            result.ErrorMessage = "Kullanıcı adı veya şifre boş olamaz.";
            return result;
        }

        try
        {
            using (var connection = new MySqlConnection(_connectionString))
            {
                await connection.OpenAsync();

                if (await IsUsernameTakenAsync(connection, usernameInput))
                {
                    result.ErrorMessage = "Bu kullanıcı adı zaten alınmış.";
                    return result;
                }

                // GÜNCELLENDİ: Parolayı Argon2 ile hash'le
                string hashedPassword = HashPasswordWithArgon2(passwordInput);

                string insertQuery = "INSERT INTO Users (UserName, UserPassword, Role, UserCreationTime) VALUES (@Username, @Password, @Role, @CreationTime)";
                using (var insertCommand = new MySqlCommand(insertQuery, connection))
                {
                    insertCommand.Parameters.AddWithValue("@Username", usernameInput);
                    insertCommand.Parameters.AddWithValue("@Password", hashedPassword); // Hash'lenmiş şifre
                    insertCommand.Parameters.AddWithValue("@Role", RoleIDs.Apprentice);
                    insertCommand.Parameters.AddWithValue("@CreationTime", DateTime.Now); //

                    int rowsAffected = await insertCommand.ExecuteNonQueryAsync();
                    if (rowsAffected > 0)
                    {
                        result.IsSuccess = true;
                    }
                    else
                    {
                        result.ErrorMessage = "Hesap oluşturulamadı (veritabanı hatası).";
                    }
                }
            }
        }
        catch (Exception ex)
        {
            HandleException(ex, result);
        }
        
        return result;
    }


    /// <summary>
    /// (Kural 1 & 2) Yalnızca 'Admin' rolündeki kullanıcıların yeni 'Admin' veya 'Usta' hesabı oluşturmasını sağlar.
    /// </summary>
    public async Task<RegistrationResult> CreateAccountByAdminAsync(string usernameInput, string passwordInput, int newAccountRoleID, int performingAdminUserID)
    {
        var result = new RegistrationResult { IsSuccess = false };

        if (newAccountRoleID != RoleIDs.Admin && newAccountRoleID != RoleIDs.Master)
        {
             result.ErrorMessage = "Adminler bu metotla sadece Admin veya Usta hesabı oluşturabilir.";
             return result;
        }

        if (string.IsNullOrEmpty(usernameInput) || string.IsNullOrEmpty(passwordInput))
        {
            result.ErrorMessage = "Kullanıcı adı veya şifre boş olamaz.";
            return result;
        }
        
        try
        {
            using (var connection = new MySqlConnection(_connectionString))
            {
                await connection.OpenAsync();

                // 1. ADIM: İşlemi yapanın 'Admin' olup olmadığını kontrol et
                string adminCheckQuery = "SELECT Role FROM Users WHERE UserID = @AdminUserID";
                int adminRole = 0;

                using (var adminCommand = new MySqlCommand(adminCheckQuery, connection))
                {
                    adminCommand.Parameters.AddWithValue("@AdminUserID", performingAdminUserID);
                    var scalarResult = await adminCommand.ExecuteScalarAsync();
                    if (scalarResult != null)
                    {
                        adminRole = Convert.ToInt32(scalarResult);
                    }
                }

                if (adminRole != RoleIDs.Admin)
                {
                    result.ErrorMessage = "Yetki hatası: Sadece Adminler yeni hesap oluşturabilir.";
                    return result;
                }

                // 2. ADIM: Yeni kullanıcı adının alınmış olup olmadığını kontrol et
                if (await IsUsernameTakenAsync(connection, usernameInput))
                {
                    result.ErrorMessage = "Bu kullanıcı adı zaten alınmış.";
                    return result;
                }

                // 3. ADIM: GÜNCELLENDİ: Parolayı Argon2 ile hash'le
                string hashedPassword = HashPasswordWithArgon2(passwordInput);

                // 4. ADIM: Yeni kullanıcıyı (Admin veya Usta) ekle
                string insertQuery = "INSERT INTO Users (UserName, UserPassword, Role, UserCreationTime) VALUES (@Username, @Password, @Role, @CreationTime)";
                using (var insertCommand = new MySqlCommand(insertQuery, connection))
                {
                    insertCommand.Parameters.AddWithValue("@Username", usernameInput);
                    insertCommand.Parameters.AddWithValue("@Password", hashedPassword); // Hash'lenmiş şifre
                    insertCommand.Parameters.AddWithValue("@Role", newAccountRoleID);
                    insertCommand.Parameters.AddWithValue("@CreationTime", DateTime.Now);

                    int rowsAffected = await insertCommand.ExecuteNonQueryAsync();
                    if (rowsAffected > 0)
                    {
                        result.IsSuccess = true;
                    }
                    else
                    {
                        result.ErrorMessage = "Hesap oluşturulamadı (veritabanı hatası).";
                    }
                }
            }
        }
        catch (Exception ex)
        {
            HandleException(ex, result);
        }

        return result;
    }


    // --- YARDIMCI METOTLAR (Argon2 için güncellendi) ---

    /// <summary>
    /// Bir parolayı Argon2id ile hash'ler ve salt ile birlikte Base64 string olarak döndürür.
    /// </summary>
    private string HashPasswordWithArgon2(string password)
    {
        // 1. Rastgele bir Salt (tuz) oluştur
        var salt = new byte[SaltSize];
        RandomNumberGenerator.Fill(salt);

        // 2. Argon2id nesnesini oluştur
        var argon2 = new Argon2id(Encoding.UTF8.GetBytes(password))
        {
            Salt = salt,
            DegreeOfParallelism = ArgonDegreeOfParallelism,
            Iterations = ArgonIterations,
            MemorySize = ArgonMemorySize
        };

        // 3. Hash'i hesapla
        var hash = argon2.GetBytes(HashSize);

        // 4. Salt ve Hash'i tek bir byte dizisinde birleştir
        // [Salt (16 byte)] + [Hash (32 byte)]
        var saltAndHash = new byte[SaltSize + HashSize];
        Buffer.BlockCopy(salt, 0, saltAndHash, 0, SaltSize);
        Buffer.BlockCopy(hash, 0, saltAndHash, SaltSize, HashSize);

        // 5. Birleşik diziyi veritabanında saklanabilir bir Base64 string'e dönüştür
        return Convert.ToBase64String(saltAndHash);
    }

    /// <summary>
    /// Sağlanan parolayı, veritabanından gelen (salt+hash) Base64 string'i ile doğrular.
    /// </summary>
    private bool VerifyPasswordWithArgon2(string password, string storedHashString)
    {
        try
        {
            // 1. Veritabanındaki Base64 string'i çözerek (salt+hash) byte dizisini elde et
            var saltAndHash = Convert.FromBase64String(storedHashString);
            
            // Boyut kontrolü
            if (saltAndHash.Length != SaltSize + HashSize)
            {
                return false;
            }

            // 2. Salt'ı diziden ayıkla (ilk 16 byte)
            var salt = new byte[SaltSize];
            Buffer.BlockCopy(saltAndHash, 0, salt, 0, SaltSize);

            // 3. Orijinal (doğru) hash'i diziden ayıkla (kalan 32 byte)
            var hashToCompare = new byte[HashSize];
            Buffer.BlockCopy(saltAndHash, SaltSize, hashToCompare, 0, HashSize);

            // 4. Kullanıcının girdiği parolayı, veritabanından aldığımız *aynı salt* ve *aynı parametrelerle*
            // yeniden hash'le
            var argon2 = new Argon2id(Encoding.UTF8.GetBytes(password))
            {
                Salt = salt,
                DegreeOfParallelism = ArgonDegreeOfParallelism,
                Iterations = ArgonIterations,
                MemorySize = ArgonMemorySize
            };

            var newHash = argon2.GetBytes(HashSize);

            // 5. Hesaplanan yeni hash ile veritabanındaki hash'i *güvenli* (zamanlama saldırılarına karşı)
            // bir şekilde karşılaştır.
            return CryptographicOperations.FixedTimeEquals(newHash, hashToCompare);
        }
        catch (Exception ex)
        {
            // Hata durumunda (örn: Base64 formatı bozuksa, eski sistemden kalma BCrypt hash'i ise)
            // güvenli olarak 'false' döndür.
            Debug.LogWarning($"Parola doğrulaması sırasında bir hata oluştu: {ex.Message}");
            return false;
        }
    }


    /// <summary>
    /// (Özel Metot) Belirtilen kullanıcı adının veritabanında zaten var olup olmadığını kontrol eder.
    /// </summary>
    private async Task<bool> IsUsernameTakenAsync(MySqlConnection connection, string username)
    {
        string checkQuery = "SELECT COUNT(1) FROM Users WHERE UserName = @Username";
        using (var checkCommand = new MySqlCommand(checkQuery, connection))
        {
            checkCommand.Parameters.AddWithValue("@Username", username);
            long userExists = (long)await checkCommand.ExecuteScalarAsync();
            return userExists > 0;
        }
    }

    /// <summary>
    /// (Özel Metot) Hata yakalamayı merkezileştirmek için.
    /// </summary>
    private void HandleException(Exception ex, RegistrationResult result)
    {
         if (ex is MySqlException myEx && myEx.Number == 1062) // Duplicate entry
         {
             result.ErrorMessage = "Bu kullanıcı adı zaten alınmış (SQL 1062).";
         }
         else
         {
             Console.WriteLine($"Bir hata oluştu: {ex.Message}");
             result.ErrorMessage = "Veritabanı hatası: " + ex.Message;
         }
    }
}