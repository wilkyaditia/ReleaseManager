using System.ComponentModel.DataAnnotations;

namespace ReleaseManager.Models;

public static class ReleaseStatus
{
    public const string ON_DEVELOPING = "ON DEVELOPING";
    public const string READY_QA = "READY QA";
    public const string ON_QA = "ON QA";
    public const string RELEASED = "RELEASED";
    public static readonly string[] All =
    {
        ON_DEVELOPING,
        READY_QA,
        ON_QA,
        RELEASED
    };

    public static bool TryGetLastVersionNumber(string? version, out int number)
    {
        number = 0;
        if (string.IsNullOrWhiteSpace(version)) return false;
        var lastPart = version.Trim().Split('.', StringSplitOptions.RemoveEmptyEntries).LastOrDefault();
        return int.TryParse(lastPart, out number);
    }

    public static string FromVersionAndChecklist(string? version, bool checklistComplete)
    {
        if (checklistComplete && TryGetLastVersionNumber(version, out var finalNumber))
            return finalNumber == 0 ? RELEASED : ON_QA;

        return ON_DEVELOPING;
    }
}
public class ProgramInfo
{
    public int Id { get; set; }
    [Required, MaxLength(100)] public string Name { get; set; } = "";
    [MaxLength(100)] public string? DisplayName { get; set; }
    [MaxLength(50)] public string CurrentVersion { get; set; } = "";
    [Required, MaxLength(500)] public string SourcePath { get; set; } = "";
    [MaxLength(500)] public string? ZipFileExtensions { get; set; }
    public int? FtpConfigId { get; set; }
    public FtpConfig? FtpConfig { get; set; }
    [MaxLength(500)] public string? FtpFolder { get; set; }
    public int? EmailConfigId { get; set; }
    public EmailConfig? EmailConfig { get; set; }
    [MaxLength(500)] public string? EmailTo { get; set; }
    [MaxLength(500)] public string? EmailCc { get; set; }
    public bool Active { get; set; } = true;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
    public ICollection<Release> Releases { get; set; } = new List<Release>();
}

public class Release
{
    public int Id { get; set; }
    public int ProgramInfoId { get; set; }
    public ProgramInfo ProgramInfo { get; set; } = null!;
    [Required, MaxLength(50)] public string Version { get; set; } = "";
    public DateTime ReleaseDate { get; set; } = DateTime.Today;
    [MaxLength(100)] public string? Pic { get; set; }
    [MaxLength(4000)] public string? Notes { get; set; }
    [Required, MaxLength(30)] public string Status { get; set; } = ReleaseStatus.ON_DEVELOPING;
    [MaxLength(300)] public string? ZipFileName { get; set; }
    [MaxLength(1000)] public string? ZipFilePath { get; set; }
    [MaxLength(1000)] public string? FtpLocation { get; set; }
    public DateTime? ZipCreatedAt { get; set; }
    public DateTime? UploadedAt { get; set; }
    public DateTime? EmailPreparedAt { get; set; }
    public DateTime? WhatsAppSentAt { get; set; }
    public DateTime? QaApprovedAt { get; set; }
    [MaxLength(100)] public string? CreatedBy { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
    public ICollection<ReleaseItem> Items { get; set; } = new List<ReleaseItem>();
}



public class ReleaseItem
{
    public int Id { get; set; }
    public int ReleaseId { get; set; }
    public Release Release { get; set; } = null!;
    [Required, MaxLength(50)] public string Category { get; set; } = "List Revisi";
    [MaxLength(50)] public string ProgramVersion { get; set; } = "";
    [MaxLength(500)] public string? ReferenceNo { get; set; }
    [Required] public string Description { get; set; } = "";
    [MaxLength(100)] public string? ComplaintSource { get; set; }
    public int SortOrder { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public int? UserId { get; set; }
    public AppUser? User { get; set; }
}

public class FtpConfig
{
    public int Id { get; set; }
    [Required, MaxLength(100)] public string Name { get; set; } = "";
    [Required, MaxLength(255)] public string Host { get; set; } = "";
    public int Port { get; set; } = 21;
    [Required, MaxLength(150)] public string Username { get; set; } = "";
    [MaxLength(1000)] public string? PasswordEncrypted { get; set; }
    public bool UseFtps { get; set; }
    public bool ValidateCertificate { get; set; } = true;
    [MaxLength(500)] public string? RemoteRoot { get; set; }
    public bool Active { get; set; } = true;
}

public class EmailConfig
{
    public int Id { get; set; }
    [Required, MaxLength(100)] public string Name { get; set; } = "";
    [Required, MaxLength(255)] public string SmtpHost { get; set; } = "";
    public int SmtpPort { get; set; } = 587;
    [MaxLength(150)] public string? Username { get; set; }
    [MaxLength(1000)] public string? PasswordEncrypted { get; set; }
    public bool UseSsl { get; set; } = true;
    [MaxLength(255)] public string FromAddress { get; set; } = "";
    [MaxLength(255)] public string? FromName { get; set; }
    [MaxLength(1000)] public string? DefaultTo { get; set; }
    [MaxLength(1000)] public string? DefaultCc { get; set; }
    public bool Active { get; set; } = true;
}

public class WhatsAppConfig
{
    public int Id { get; set; }
    [Required, MaxLength(100)] public string Name { get; set; } = "";
    [MaxLength(30)] public string? DefaultPhone { get; set; }
    [MaxLength(500)] public string? ApiUrl { get; set; }
    [MaxLength(2000)] public string? AccessTokenEncrypted { get; set; }
    public bool Active { get; set; } = true;
}

public class AppUser
{
    public int Id { get; set; }
    [Required, MaxLength(100)] public string Username { get; set; } = "";
    [Required, MaxLength(255)] public string PasswordHash { get; set; } = "";
    [Required, MaxLength(100)] public string DisplayName { get; set; } = "";
    [Required, MaxLength(30)] public string Role { get; set; } = "Developer";
    [MaxLength(4000)] public string? WhatsAppMessageFormat { get; set; }
    public bool Active { get; set; } = true;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

public class AuditLog
{
    public long Id { get; set; }
    [MaxLength(100)] public string? Username { get; set; }
    [Required, MaxLength(50)] public string Action { get; set; } = "";
    [MaxLength(100)] public string? Entity { get; set; }
    public int? EntityId { get; set; }
    [MaxLength(2000)] public string? Description { get; set; }
    [MaxLength(100)] public string? IpAddress { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

