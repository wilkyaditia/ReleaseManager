using System.ComponentModel.DataAnnotations;
using ReleaseManager.Models;

namespace ReleaseManager.ViewModels;

public static class ReleaseCategories
{
    public const string PRPK = "PRPK";
    public const string MEMO = "MEMO";
    public const string WAITING_MEMO = "WAITING MEMO";
    public const string WAITING_PRPK = "WAITING PRPK";
    public const string REVISI = "REVISI";

    public static readonly string[] All =
    {
        PRPK,
        MEMO,
        WAITING_MEMO,
        WAITING_PRPK,
        REVISI
    };
}

public class LoginViewModel
{
    [Required] public string Username { get; set; } = "";
    [Required, DataType(DataType.Password)] public string Password { get; set; } = "";
}

public static class UserRoles
{
    public const string Admin = "Admin";
    public const string QA = "QA";
    public const string Developer = "Developer";

    public static readonly string[] All = { Admin, QA, Developer };
}

public class RegisterUserViewModel
{
    [Required, MaxLength(100)] public string Username { get; set; } = "";
    [Required, MaxLength(100)] public string DisplayName { get; set; } = "";
    [Required] public string Role { get; set; } = UserRoles.Developer;
    [Required, MinLength(8), DataType(DataType.Password)] public string Password { get; set; } = "";
    [Required, Compare(nameof(Password)), DataType(DataType.Password)] public string ConfirmPassword { get; set; } = "";
}

public class EditUserViewModel
{
    public int Id { get; set; }
    [Required, MaxLength(100)] public string Username { get; set; } = "";
    [Required, MaxLength(100)] public string DisplayName { get; set; } = "";
    [Required] public string Role { get; set; } = UserRoles.Developer;
    public bool Active { get; set; }
    [MinLength(8), DataType(DataType.Password)] public string? NewPassword { get; set; }
    [DataType(DataType.Password)] public string? ConfirmPassword { get; set; }
}

public class ProgramFormViewModel
{
    public int Id { get; set; }
    [Required, MaxLength(100)] public string Name { get; set; } = "";
    [MaxLength(100)] public string? DisplayName { get; set; }
    [MaxLength(50)] public string CurrentVersion { get; set; } = "";
    [Required, MaxLength(500)] public string SourcePath { get; set; } = "";
    [MaxLength(500)] public string? ZipFileExtensions { get; set; }
    public int? FtpConfigId { get; set; }
    [MaxLength(500)] public string? FtpFolder { get; set; }
    public int? EmailConfigId { get; set; }
    [MaxLength(500)] public string? EmailTo { get; set; }
    [MaxLength(500)] public string? EmailCc { get; set; }
    public bool Active { get; set; } = true;
}
public class ReleaseFormViewModel
{
    public int Id { get; set; }
    [Required] public int ProgramInfoId { get; set; }
    [Required] public string Version { get; set; } = "";
    [Required] public DateTime ReleaseDate { get; set; } = DateTime.Today;
    public DateTime? CreatedAt { get; set; }
    public DateTime? LastUpdatedAt { get; set; }
    public string? Pic { get; set; }
    [MaxLength(4000)] public string? Notes { get; set; }
    public string Status { get; set; } = ReleaseStatus.ON_DEVELOPING;
    public List<ReleaseItemForm> Items { get; set; } = new();
}
public class ReleaseItemForm
{
    public int Id { get; set; }
    public string Category { get; set; } = ReleaseCategories.PRPK;
    [MaxLength(50)] public string ProgramVersion { get; set; } = "";
    public string? ReferenceNo { get; set; }
    public string Description { get; set; } = "";
    public string? ComplaintSource { get; set; }
    public int SortOrder { get; set; }
    public DateTime CreatedAt { get; set; }
    public string? UserDisplayName { get; set; }
}
public class FtpFormViewModel
{
    public int Id { get; set; }
    [Required] public string Name { get; set; } = "";
    [Required] public string Host { get; set; } = "";
    public int Port { get; set; } = 21;
    [Required] public string Username { get; set; } = "";
    public string? Password { get; set; }
    public bool UseFtps { get; set; }
    public bool ValidateCertificate { get; set; } = true;
    public string? RemoteRoot { get; set; }
    public bool Active { get; set; } = true;
}
public class EmailFormViewModel
{
    public int Id { get; set; }
    [Required] public string Name { get; set; } = "";
    [Required] public string SmtpHost { get; set; } = "";
    public int SmtpPort { get; set; } = 587;
    public string? Username { get; set; }
    public string? Password { get; set; }
    public bool UseSsl { get; set; } = true;
    [Required] public string FromAddress { get; set; } = "";
    public string? FromName { get; set; }
    public string? DefaultTo { get; set; }
    public string? DefaultCc { get; set; }
    public bool Active { get; set; } = true;
}
