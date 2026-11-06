using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using ReleaseManager.Data;
using ReleaseManager.Models;
using ReleaseManager.Services;
using ReleaseManager.ViewModels;

namespace ReleaseManager.Controllers;

[Authorize(Roles = "Admin")]
public class SettingsController : Controller
{
    private readonly AppDbContext _db;
    private readonly SecretService _secret;

    public SettingsController(AppDbContext db, SecretService secret)
    {
        _db = db;
        _secret = secret;
    }

    public async Task<IActionResult> Index() =>
        View((await _db.FtpConfigs.ToListAsync(), await _db.EmailConfigs.ToListAsync()));

    public IActionResult FtpCreate() => View("Ftp", new FtpFormViewModel());

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> FtpSave(FtpFormViewModel m)
    {
        if (m.Port is < 1 or > 65535) ModelState.AddModelError(nameof(m.Port), "Port must be between 1 and 65535.");
        if (!ModelState.IsValid) return View("Ftp", m);

        FtpConfig e;
        if (m.Id == 0) e = new FtpConfig();
        else e = await _db.FtpConfigs.FindAsync(m.Id) ?? null!;
        if (e is null) return NotFound();

        e.Name = m.Name.Trim();
        e.Host = m.Host.Trim();
        e.Port = m.Port;
        e.Username = m.Username.Trim();
        e.UseFtps = m.UseFtps;
        e.ValidateCertificate = m.ValidateCertificate;
        e.RemoteRoot = m.RemoteRoot;
        e.Active = m.Active;
        if (!string.IsNullOrWhiteSpace(m.Password)) e.PasswordEncrypted = _secret.Protect(m.Password);
        if (m.Id == 0) _db.FtpConfigs.Add(e);
        await _db.SaveChangesAsync();
        return RedirectToAction(nameof(Index));
    }

    public async Task<IActionResult> FtpEdit(int id)
    {
        var e = await _db.FtpConfigs.FindAsync(id);
        if (e is null) return NotFound();
        return View("Ftp", new FtpFormViewModel { Id = e.Id, Name = e.Name, Host = e.Host, Port = e.Port, Username = e.Username, UseFtps = e.UseFtps, ValidateCertificate = e.ValidateCertificate, RemoteRoot = e.RemoteRoot, Active = e.Active });
    }

    public IActionResult EmailCreate() => View("Email", new EmailFormViewModel());

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> EmailSave(EmailFormViewModel m)
    {
        if (m.SmtpPort is < 1 or > 65535) ModelState.AddModelError(nameof(m.SmtpPort), "Port must be between 1 and 65535.");
        if (!ModelState.IsValid) return View("Email", m);

        EmailConfig e;
        if (m.Id == 0) e = new EmailConfig();
        else e = await _db.EmailConfigs.FindAsync(m.Id) ?? null!;
        if (e is null) return NotFound();

        e.Name = m.Name.Trim();
        e.SmtpHost = m.SmtpHost.Trim();
        e.SmtpPort = m.SmtpPort;
        e.Username = m.Username;
        e.UseSsl = m.UseSsl;
        e.FromAddress = m.FromAddress.Trim();
        e.FromName = m.FromName;
        e.DefaultTo = m.DefaultTo;
        e.DefaultCc = m.DefaultCc;
        e.Active = m.Active;
        if (!string.IsNullOrWhiteSpace(m.Password)) e.PasswordEncrypted = _secret.Protect(m.Password);
        if (m.Id == 0) _db.EmailConfigs.Add(e);
        await _db.SaveChangesAsync();
        return RedirectToAction(nameof(Index));
    }

    public async Task<IActionResult> EmailEdit(int id)
    {
        var e = await _db.EmailConfigs.FindAsync(id);
        if (e is null) return NotFound();
        return View("Email", new EmailFormViewModel { Id = e.Id, Name = e.Name, SmtpHost = e.SmtpHost, SmtpPort = e.SmtpPort, Username = e.Username, UseSsl = e.UseSsl, FromAddress = e.FromAddress, FromName = e.FromName, DefaultTo = e.DefaultTo, DefaultCc = e.DefaultCc, Active = e.Active });
    }
}
