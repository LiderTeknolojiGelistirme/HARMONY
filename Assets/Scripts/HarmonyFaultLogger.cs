using System;
using System.Globalization;
using System.IO;
using System.Text;
using UnityEngine;

/// <summary>
/// Senaryo uyarısının operatör tarafından ne kadar sürede kapatıldığını
/// (KPI3 tepki süresi) cihaz diskine CSV olarak yazar.
///
/// Dosya <see cref="Application.persistentDataPath"/> altına açılır; Android'de
/// bu yol uygulamaya özel harici depolamaya karşılık gelir ve adb ile
/// çekilebilir.
///
/// Her uygulama açılışı KENDİ dosyasına yazar: dosya adının sonuna açılış
/// zamanı eklenir (harmony_kpi3_20260729_051533.csv). Böylece oturumlar
/// fiziksel olarak ayrışır, tek bir dosyanın bozulması diğer oturumların
/// verisini götürmez ve birden fazla katılımcıyla test ederken kayıtlar
/// karışmaz.
///
/// Süre ölçümü <see cref="System.Diagnostics.Stopwatch"/> ile yapılır: duvar
/// saati (DateTime) cihaz saati güncellenirse veya zaman dilimi değişirse
/// sıçrayabilir, monotonik sayaç sıçramaz. Duvar saati yalnızca okunabilir
/// zaman damgası olarak yazılır.
/// </summary>
public class HarmonyFaultLogger
{
    /// <summary>Zaman damgası biçimi — milisaniye dahil, ROS log'larıyla eşleştirilebilir.</summary>
    private const string TimeFormat = "yyyy-MM-dd'T'HH:mm:ss.fff";

    private const string Header =
        "app_start,seq,code,fault_time,ack_time,reaction_seconds,acknowledged";

    private readonly string filePath;
    private readonly string appStartText;

    /// <summary>Kayıt dosyasının tam yolu; konsola basmak ve adb ile çekmek için.</summary>
    public string FilePath => filePath;

    /// <summary>Dosya adına eklenen açılış zamanı damgası.</summary>
    private const string StampFormat = "yyyyMMdd_HHmmss";

    /// <summary>
    /// Yazıcıyı kurar. Dosya adının sonuna açılış zamanı eklenir ve dosya yeni
    /// ise başlık satırı yazılır.
    /// </summary>
    /// <param name="fileName">
    /// Temel dosya adı, örn. "harmony_kpi3.csv". Zaman damgası uzantıdan önce
    /// eklenir: "harmony_kpi3_20260729_051533.csv".
    /// </param>
    /// <param name="appStart">Uygulamanın açılış zamanı.</param>
    public HarmonyFaultLogger(string fileName, DateTime appStart)
    {
        string safeName = string.IsNullOrWhiteSpace(fileName) ? "harmony_kpi3.csv" : fileName.Trim();

        string baseName = Path.GetFileNameWithoutExtension(safeName);
        string extension = Path.GetExtension(safeName);
        if (string.IsNullOrEmpty(baseName)) baseName = "harmony_kpi3";
        if (string.IsNullOrEmpty(extension)) extension = ".csv";

        string stamp = appStart.ToString(StampFormat, CultureInfo.InvariantCulture);
        filePath = Path.Combine(Application.persistentDataPath, baseName + "_" + stamp + extension);

        appStartText = appStart.ToString(TimeFormat, CultureInfo.InvariantCulture);

        try
        {
            if (!File.Exists(filePath))
                File.WriteAllText(filePath, Header + Environment.NewLine, new UTF8Encoding(false));
        }
        catch (Exception e)
        {
            // Yazma başarısız olsa bile uyarı akışı kesilmemeli.
            Debug.LogWarning($"HarmonyFaultLogger: '{filePath}' hazırlanamadı ({e.Message}).");
        }
    }

    /// <summary>
    /// Tamamlanmış bir uyarı turunu yazar.
    /// </summary>
    /// <param name="seq">Uyarının bu oturumdaki sıra numarası.</param>
    /// <param name="code">Hata kodu, örn. "E-1042".</param>
    /// <param name="faultTime">Uyarının çıktığı an (T0).</param>
    /// <param name="ackTime">Operatörün OK'ye bastığı an; kapatılmadıysa null.</param>
    /// <param name="reactionSeconds">
    /// T0 ile OK arasında geçen süre [s], monotonik sayaçtan; kapatılmadıysa null.
    /// </param>
    public void Write(int seq, string code, DateTime faultTime, DateTime? ackTime, double? reactionSeconds)
    {
        var row = new StringBuilder();
        row.Append(appStartText).Append(',');
        row.Append(seq.ToString(CultureInfo.InvariantCulture)).Append(',');
        row.Append(Escape(code)).Append(',');
        row.Append(faultTime.ToString(TimeFormat, CultureInfo.InvariantCulture)).Append(',');
        row.Append(ackTime.HasValue
            ? ackTime.Value.ToString(TimeFormat, CultureInfo.InvariantCulture)
            : string.Empty).Append(',');
        row.Append(reactionSeconds.HasValue
            ? reactionSeconds.Value.ToString("0.000", CultureInfo.InvariantCulture)
            : string.Empty).Append(',');
        row.Append(ackTime.HasValue ? "yes" : "no");

        try
        {
            // Her satır anında diske yazılır; uygulama beklenmedik kapanırsa
            // ölçüm kaybolmasın.
            File.AppendAllText(filePath, row + Environment.NewLine, new UTF8Encoding(false));
            Debug.Log($"HarmonyFaultLogger: kayıt eklendi → {row}");
        }
        catch (Exception e)
        {
            Debug.LogWarning($"HarmonyFaultLogger: satır yazılamadı ({e.Message}).");
        }
    }

    /// <summary>Virgül veya tırnak içeren alanı CSV kuralına göre kaçırır.</summary>
    /// <param name="value">Ham alan değeri.</param>
    private static string Escape(string value)
    {
        if (string.IsNullOrEmpty(value)) return string.Empty;
        if (value.IndexOf(',') < 0 && value.IndexOf('"') < 0 && value.IndexOf('\n') < 0)
            return value;
        return "\"" + value.Replace("\"", "\"\"") + "\"";
    }
}
