using System;
using Newtonsoft.Json;
using UnityEngine;

// =============================================================================
// HARMONY senaryosunun ROS tarafıyla konuşurken kullandığı veri sözleşmeleri.
//
// Kaynak: pymoveit2_real/pymoveit2_real/harmony_defects.py
//   - defect_payload()  -> HarmonyDefectDto
//   - status_payload()  -> HarmonyDefectStatusDto
// Tüm mesajlar std_msgs/String gövdesinde JSON olarak taşınır; özel ROS mesaj
// tipi yoktur, bu yüzden ROS# tarafında String alıp burada parse ediyoruz.
// =============================================================================

/// <summary>
/// Bir kusurun yaşam döngüsündeki durumu.
/// ROS tarafındaki STATUS_* sabitleriyle birebir eşleşir.
/// </summary>
public enum DefectStatus
{
    /// <summary>Sensing robotu tespit etti, henüz temizlenmedi.</summary>
    Detected,

    /// <summary>Cleaning robotu üzerinde çalışıyor.</summary>
    Cleaning,

    /// <summary>Giderim tamamlandı.</summary>
    Cleaned,

    /// <summary>
    /// Hedefe gidilemedi (MoveIt planlayamadı / execute başarısız).
    /// Ayrı bir durum olması önemli: Detected'a geri dönerse işaretçi tespit
    /// renginde kalır ve hata gözden kaçar.
    /// </summary>
    Failed,

    /// <summary>Tanınmayan bir durum metni geldi.</summary>
    Unknown
}

/// <summary>
/// JSON'daki iç içe geçmiş "position" / "approach" nesnesi.
/// </summary>
[Serializable]
public class HarmonyPointDto
{
    [JsonProperty("x")] public double X;
    [JsonProperty("y")] public double Y;
    [JsonProperty("z")] public double Z;

    /// <summary>Ham ROS koordinatını (dönüşümsüz) Vector3 olarak verir.</summary>
    public Vector3 ToRosVector() => new Vector3((float)X, (float)Y, (float)Z);
}

/// <summary>
/// /harmony/mock_perception/defect ve /defect_list topic'lerinden gelen gövde.
/// </summary>
[Serializable]
public class HarmonyDefectDto
{
    [JsonProperty("timestamp")] public string Timestamp;
    [JsonProperty("defect_id")] public string DefectId;
    [JsonProperty("defect_type")] public string DefectType;
    [JsonProperty("frame_id")] public string FrameId;
    [JsonProperty("position")] public HarmonyPointDto Position;
    [JsonProperty("approach")] public HarmonyPointDto Approach;
    [JsonProperty("status")] public string Status;
}

/// <summary>
/// /harmony/defect_status topic'inden gelen durum güncellemesi.
/// </summary>
[Serializable]
public class HarmonyDefectStatusDto
{
    [JsonProperty("defect_id")] public string DefectId;
    [JsonProperty("status")] public string Status;
}

/// <summary>
/// /harmony/robot_status topic'inden gelen görev durumu.
/// </summary>
[Serializable]
public class HarmonyRobotStatusDto
{
    [JsonProperty("state")] public string State;
    [JsonProperty("note")] public string Note;
}

/// <summary>
/// /harmony/cmd_input topic'ine gönderilen operatör komutu.
/// </summary>
[Serializable]
public class HarmonyCommandDto
{
    [JsonProperty("cmd")] public string Cmd;
}

/// <summary>
/// Arayüzün kullandığı çalışma zamanı kusur modeli.
/// DTO'dan üretilir; koordinat dönüşümünü tembel (lazy) yapar, çünkü AR
/// tarafında anchor hizalaması değiştiğinde yeniden hesaplanması gerekebilir.
/// </summary>
public class HarmonyDefect
{
    /// <summary>Kusur kimliği, örn. "DEF-01".</summary>
    public string Id { get; private set; }

    /// <summary>Kusur tipi, senaryoda "Protrusion" veya "Dent".</summary>
    public string DefectType { get; private set; }

    /// <summary>Koordinatların tanımlı olduğu ROS frame'i, senaryoda "world".</summary>
    public string FrameId { get; private set; }

    /// <summary>Kusurun ham ROS koordinatı (metre).</summary>
    public Vector3 RosPosition { get; private set; }

    /// <summary>Robotun kusura yaklaşırken durduğu ham ROS koordinatı (metre).</summary>
    public Vector3 RosApproach { get; private set; }

    /// <summary>Güncel yaşam döngüsü durumu.</summary>
    public DefectStatus Status { get; internal set; }

    /// <summary>Kusurun Unity dünya koordinatındaki karşılığı.</summary>
    public Vector3 UnityPosition =>
        RosUnityCoordinateConverter.RosToUnityPosition(RosPosition.x, RosPosition.y, RosPosition.z);

    /// <summary>Yaklaşma noktasının Unity dünya koordinatındaki karşılığı.</summary>
    public Vector3 UnityApproach =>
        RosUnityCoordinateConverter.RosToUnityPosition(RosApproach.x, RosApproach.y, RosApproach.z);

    /// <summary>
    /// ROS'tan gelen DTO'yu çalışma zamanı modeline çevirir.
    /// </summary>
    /// <param name="dto">Parse edilmiş JSON gövdesi.</param>
    /// <returns>Model nesnesi; DTO geçersizse null.</returns>
    public static HarmonyDefect FromDto(HarmonyDefectDto dto)
    {
        if (dto == null || string.IsNullOrEmpty(dto.DefectId))
            return null;

        return new HarmonyDefect
        {
            Id = dto.DefectId.Trim(),
            DefectType = dto.DefectType ?? string.Empty,
            FrameId = string.IsNullOrEmpty(dto.FrameId) ? "world" : dto.FrameId,
            RosPosition = dto.Position != null ? dto.Position.ToRosVector() : Vector3.zero,
            RosApproach = dto.Approach != null ? dto.Approach.ToRosVector() : Vector3.zero,
            Status = ParseStatus(dto.Status)
        };
    }

    /// <summary>
    /// ROS'tan gelen durum metnini enum'a çevirir. Tanınmayan metin
    /// <see cref="DefectStatus.Unknown"/> döner; senaryo akışını kırmaz.
    /// </summary>
    /// <param name="raw">Ham durum metni, örn. "CLEANING".</param>
    public static DefectStatus ParseStatus(string raw)
    {
        if (string.IsNullOrEmpty(raw))
            return DefectStatus.Unknown;

        switch (raw.Trim().ToUpperInvariant())
        {
            case "DETECTED": return DefectStatus.Detected;
            case "CLEANING": return DefectStatus.Cleaning;
            case "CLEANED": return DefectStatus.Cleaned;
            case "FAILED": return DefectStatus.Failed;
            default: return DefectStatus.Unknown;
        }
    }

    /// <summary>
    /// DTO'daki alanları mevcut model üzerine yazar. Durum bilgisi ayrı bir
    /// topic'ten (/harmony/defect_status) gelebildiği için burada ezilmez.
    /// </summary>
    /// <param name="dto">Yeni gelen gövde.</param>
    internal void UpdateGeometry(HarmonyDefectDto dto)
    {
        if (dto == null) return;

        if (!string.IsNullOrEmpty(dto.DefectType)) DefectType = dto.DefectType;
        if (!string.IsNullOrEmpty(dto.FrameId)) FrameId = dto.FrameId;
        if (dto.Position != null) RosPosition = dto.Position.ToRosVector();
        if (dto.Approach != null) RosApproach = dto.Approach.ToRosVector();
    }
}
