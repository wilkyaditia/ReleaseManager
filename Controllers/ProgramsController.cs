using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using ReleaseManager.Data;
using ReleaseManager.Models;
using ReleaseManager.ViewModels;

namespace ReleaseManager.Controllers;

[Authorize]
public class ProgramsController : Controller
{
    private readonly AppDbContext _db;
    private readonly ILogger<ProgramsController> _logger;

    public ProgramsController(AppDbContext db, ILogger<ProgramsController> logger)
    {
        _db = db;
        _logger = logger;
    }
    public async Task<IActionResult> Index() => View(await _db.Programs.Include(x => x.FtpConfig).Include(x => x.EmailConfig).OrderBy(x => x.Name).ToListAsync());
    [Authorize(Roles = "Admin")] public async Task<IActionResult> Create() { ViewBag.Ftp = await _db.FtpConfigs.Where(x => x.Active).ToListAsync(); ViewBag.Email = await _db.EmailConfigs.Where(x => x.Active).ToListAsync(); return View(new ProgramFormViewModel()); }
    [HttpPost, Authorize(Roles = "Admin"), ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(ProgramFormViewModel m)
    {
        m.Name = m.Name?.Trim() ?? string.Empty;
        m.ZipFileExtensions = NormalizeZipFileExtensions(m.ZipFileExtensions);
        if (ModelState.IsValid && await _db.Programs.AnyAsync(x => x.Name == m.Name))
            ModelState.AddModelError(nameof(m.Name), "A program with this name already exists.");

        if (!ModelState.IsValid || !await ConfigurationsExist(m.FtpConfigId, m.EmailConfigId))
        {
            await Load();
            return View(m);
        }

        var program = new ProgramInfo
        {
            Name = m.Name,
            DisplayName = m.DisplayName,
            CurrentVersion = m.CurrentVersion,
            SourcePath = m.SourcePath.Trim(),
            ZipFileExtensions = m.ZipFileExtensions,
            FtpConfigId = m.FtpConfigId,
            FtpFolder = m.FtpFolder,
            EmailConfigId = m.EmailConfigId,
            EmailTo = m.EmailTo,
            EmailCc = m.EmailCc,
            Active = m.Active
        };

        _db.Programs.Add(program);
        try
        {
            await _db.SaveChangesAsync();
        }
        catch (DbUpdateException ex)
        {
            _logger.LogError(ex, "Unable to create program {ProgramName}", program.Name);
            ModelState.AddModelError(string.Empty, "The program could not be saved. Check that the name is unique and try again. If it continues, contact an administrator with the server log details.");
            await Load();
            return View(m);
        }
        return RedirectToAction(nameof(Index));
    }
    [Authorize(Roles = "Admin")] public async Task<IActionResult> Edit(int id) { var e = await _db.Programs.FindAsync(id); if (e == null) return NotFound(); await Load(); return View(ToVm(e)); }
    [HttpPost, Authorize(Roles = "Admin"), ValidateAntiForgeryToken]
    public async Task<IActionResult> Edit(ProgramFormViewModel m)
    {
        var program = await _db.Programs.FindAsync(m.Id);
        if (program is null) return NotFound();

        m.Name = m.Name?.Trim() ?? string.Empty;
        m.ZipFileExtensions = NormalizeZipFileExtensions(m.ZipFileExtensions);
        if (ModelState.IsValid && await _db.Programs.AnyAsync(x => x.Id != m.Id && x.Name == m.Name))
            ModelState.AddModelError(nameof(m.Name), "A program with this name already exists.");

        if (!ModelState.IsValid || !await ConfigurationsExist(m.FtpConfigId, m.EmailConfigId))
        {
            await Load();
            return View(m);
        }

        program.Name = m.Name;
        program.DisplayName = m.DisplayName;
        program.CurrentVersion = m.CurrentVersion;
        program.SourcePath = m.SourcePath.Trim();
        program.ZipFileExtensions = m.ZipFileExtensions;
        program.FtpConfigId = m.FtpConfigId;
        program.FtpFolder = m.FtpFolder;
        program.EmailConfigId = m.EmailConfigId;
        program.EmailTo = m.EmailTo;
        program.EmailCc = m.EmailCc;
        program.Active = m.Active;
        program.UpdatedAt = DateTime.UtcNow;
        try
        {
            await _db.SaveChangesAsync();
        }
        catch (DbUpdateException ex)
        {
            _logger.LogError(ex, "Unable to update program {ProgramName} ({ProgramId})", program.Name, program.Id);
            ModelState.AddModelError(string.Empty, "The program could not be saved. Check that the name is unique and try again. If it continues, contact an administrator with the server log details.");
            await Load();
            return View(m);
        }
        return RedirectToAction(nameof(Index));
    }
    private async Task Load() { ViewBag.Ftp = await _db.FtpConfigs.Where(x => x.Active).ToListAsync(); ViewBag.Email = await _db.EmailConfigs.Where(x => x.Active).ToListAsync(); }
    private async Task<bool> ConfigurationsExist(int? ftpId, int? emailId)
    {
        var valid = (!ftpId.HasValue || await _db.FtpConfigs.AnyAsync(x => x.Id == ftpId && x.Active)) &&
                    (!emailId.HasValue || await _db.EmailConfigs.AnyAsync(x => x.Id == emailId && x.Active));
        if (!valid) ModelState.AddModelError(string.Empty, "Choose an active FTP and email configuration.");
        return valid;
    }
    private string? NormalizeZipFileExtensions(string? input)
    {
        if (string.IsNullOrWhiteSpace(input)) return null;
        var extensions = input.Split(new[] { ',', ';', ' ', '\n', '\r', '\t' },
                StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(x => x.TrimStart('.').ToLowerInvariant())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (extensions.Any(x => x.Length == 0 || x.Length > 50 ||
            !x.All(c => char.IsLetterOrDigit(c) || c is '.' or '_' or '-')))
        {
            ModelState.AddModelError(nameof(ProgramFormViewModel.ZipFileExtensions),
                "Enter file extensions such as .dll, .config, .json. Separate multiple extensions with commas.");
            return input;
        }

        return string.Join(", ", extensions.Select(x => "." + x));
    }
    private static ProgramFormViewModel ToVm(ProgramInfo e) => new()
    { Id = e.Id, Name = e.Name, DisplayName = e.DisplayName, CurrentVersion = e.CurrentVersion, SourcePath = e.SourcePath, ZipFileExtensions = e.ZipFileExtensions, FtpConfigId = e.FtpConfigId, FtpFolder = e.FtpFolder, EmailConfigId = e.EmailConfigId, EmailTo = e.EmailTo, EmailCc = e.EmailCc, Active = e.Active };
}
