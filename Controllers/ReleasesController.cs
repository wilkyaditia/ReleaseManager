
using System.Security.Claims;
using FluentFTP;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using ReleaseManager.Data;
using ReleaseManager.Models;
using ReleaseManager.Services;
using ReleaseManager.ViewModels;

namespace ReleaseManager.Controllers;

[Authorize]
public class ReleasesController : Controller
{
    private readonly AppDbContext _db;
    private readonly ReleaseService _release;
    private readonly FtpService _ftp;
    private readonly AuditService _audit;
    private readonly ReleaseActionProgressStore _progress;
    private readonly ILogger<ReleasesController> _logger;

    public ReleasesController(
        AppDbContext db,
        ReleaseService release,
        FtpService ftp,
        AuditService audit,
        ReleaseActionProgressStore progress,
        ILogger<ReleasesController> logger)
    {
        _db = db;
        _release = release;
        _ftp = ftp;
        _audit = audit;
        _progress = progress;
        _logger = logger;
    }

    // =========================================================
    // INDEX
    // =========================================================

    public async Task<IActionResult> Index(string? status)
    {
        var releases = await _db.Releases
            .Include(x => x.ProgramInfo)
            .Include(x => x.Items)
            .OrderByDescending(x => x.ReleaseDate)
            .ThenByDescending(x => x.Id)
            .ToListAsync();

        foreach (var release in releases)
            RefreshReleaseStatus(release);
        await _db.SaveChangesAsync();

        if (!string.IsNullOrWhiteSpace(status))
            releases = releases.Where(x => x.Status == status).ToList();

        ViewBag.Status = status;
        return View(releases);
    }

    // =========================================================
    // CREATE - GET
    // =========================================================

    public async Task<IActionResult> Create()
    {
        await LoadPrograms();

        return View(new ReleaseFormViewModel());
    }

    // =========================================================
    // CREATE - POST
    // =========================================================

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(ReleaseFormViewModel m)
    {
        if (m.Status == ReleaseStatus.RELEASED)
        {
            ModelState.AddModelError(nameof(m.Status), "Release status is assigned automatically after all release actions are complete.");
            await LoadPrograms();
            return View(m);
        }

        if (!ModelState.IsValid)
        {
            await LoadPrograms();
            return View(m);
        }

        if (!ReleaseStatus.All.Contains(m.Status))
        {
            ModelState.AddModelError(nameof(m.Status), "Select a valid release status.");
            await LoadPrograms();
            return View(m);
        }

        bool versionExists = await _db.Releases.AnyAsync(x =>
            x.ProgramInfoId == m.ProgramInfoId &&
            x.Version == m.Version
        );

        if (versionExists)
        {
            ModelState.AddModelError(
                "Version",
                "Version already exists for this program."
            );

            await LoadPrograms();
            return View(m);
        }

        if (!await _db.Programs.AnyAsync(x => x.Id == m.ProgramInfoId && x.Active))
        {
            ModelState.AddModelError(nameof(m.ProgramInfoId), "Select an active program.");
            await LoadPrograms();
            return View(m);
        }

        var submittedItems = m.Items.Where(x => !string.IsNullOrWhiteSpace(x.Description)).ToList();
        if (submittedItems.Any(x => string.IsNullOrWhiteSpace(x.ProgramVersion)))
        {
            ModelState.AddModelError(nameof(m.Items), "Enter a program version for each release item.");
            await LoadPrograms();
            return View(m);
        }
        if (submittedItems.Any(x => !ReleaseCategories.All.Contains(x.Category)))
        {
            ModelState.AddModelError(nameof(m.Items), "An item has an invalid category.");
            await LoadPrograms();
            return View(m);
        }

        var e = new Release
        {
            ProgramInfoId = m.ProgramInfoId,
            Version = m.Version,
            ReleaseDate = m.ReleaseDate,
            Pic = m.Pic,
            Notes = m.Notes,
            CreatedBy = User.Identity?.Name,
            Status = ReleaseStatus.FromVersionAndChecklist(m.Version, checklistComplete: false)
        };

        var currentUserId = await GetCurrentUserIdAsync();
        foreach (var i in submittedItems)
        {
            e.Items.Add(new ReleaseItem
            {
                Category = i.Category,
                ProgramVersion = i.ProgramVersion.Trim(),
                ReferenceNo = i.Category == ReleaseCategories.REVISI ? null : i.ReferenceNo,
                Description = i.Description,
                ComplaintSource = i.Category == ReleaseCategories.REVISI ? i.ComplaintSource : null,
                SortOrder = i.SortOrder,
                CreatedAt = DateTime.UtcNow,
                UserId = currentUserId
            });
        }

        _db.Releases.Add(e);

        await _db.SaveChangesAsync();

        await _audit.LogAsync(
            "CREATE_RELEASE",
            "Release",
            e.Id,
            e.Version
        );

        return RedirectToAction(
            nameof(Details),
            new { id = e.Id }
        );
    }

    // =========================================================
    // DETAILS
    // =========================================================

    public async Task<IActionResult> Details(int id)
    {
        var e = await Get(id);

        if (e == null)
            return NotFound();

        RefreshReleaseStatus(e);
        await _db.SaveChangesAsync();

        ViewBag.ZipExists = ZipFileExists(e);

        return View(e);
    }

    // =========================================================
    // EDIT - GET
    // =========================================================

    public async Task<IActionResult> Edit(int id)
    {
        var e = await Get(id);

        if (e == null)
            return NotFound();

        await LoadPrograms();

        return View(ToVm(e));
    }

    // =========================================================
    // EDIT - POST
    // =========================================================

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Edit(ReleaseFormViewModel m)
    {
        var e = await _db.Releases
            .Include(x => x.Items)
            .Include(x => x.ProgramInfo)
            .FirstOrDefaultAsync(x => x.Id == m.Id);

        if (e == null)
            return NotFound();

        m.CreatedAt = e.CreatedAt;
        m.LastUpdatedAt = e.Items.Select(x => (DateTime?)x.CreatedAt).Max();

        if (!ModelState.IsValid)
        {
            await LoadPrograms();
            return View(m);
        }

        if (!ReleaseStatus.All.Contains(m.Status))
        {
            ModelState.AddModelError(nameof(m.Status), "Select a valid release status.");
            await LoadPrograms();
            return View(m);
        }

        if (m.Status == ReleaseStatus.RELEASED && e.Status != ReleaseStatus.RELEASED)
        {
            ModelState.AddModelError(nameof(m.Status), "Release status is assigned automatically after all release actions are complete.");
            await LoadPrograms();
            return View(m);
        }

        bool versionExists = await _db.Releases.AnyAsync(x =>
            x.Id != m.Id &&
            x.ProgramInfoId == m.ProgramInfoId &&
            x.Version == m.Version
        );

        if (versionExists)
        {
            ModelState.AddModelError(
                "Version",
                "Version already exists for this program."
            );

            await LoadPrograms();
            return View(m);
        }

        if (!await _db.Programs.AnyAsync(x => x.Id == m.ProgramInfoId && x.Active))
        {
            ModelState.AddModelError(nameof(m.ProgramInfoId), "Select an active program.");
            await LoadPrograms();
            return View(m);
        }

        var programOrVersionChanged = e.ProgramInfoId != m.ProgramInfoId ||
                                      !string.Equals(e.Version, m.Version, StringComparison.OrdinalIgnoreCase);

        // A new program or version invalidates the previous package and checklist.
        if (programOrVersionChanged)
        {
            e.ZipFileName = null;
            e.ZipFilePath = null;
            e.ZipCreatedAt = null;
            e.UploadedAt = null;
            e.FtpLocation = null;
            e.EmailPreparedAt = null;
            e.WhatsAppSentAt = null;
            e.QaApprovedAt = null;
        }

        // Update release information
        e.ProgramInfoId = m.ProgramInfoId;
        e.Version = m.Version;
        e.ReleaseDate = m.ReleaseDate;
        e.Pic = m.Pic;
        e.Notes = m.Notes;
        e.Status = m.Status == ReleaseStatus.READY_QA
            ? ReleaseStatus.READY_QA
            : ReleaseStatus.FromVersionAndChecklist(e.Version, IsReleaseChecklistComplete(e));
        e.UpdatedAt = DateTime.UtcNow;

        // Get submitted items
        var submittedItems = m.Items
            .Where(x => !string.IsNullOrWhiteSpace(x.Description))
            .ToList();

        if (submittedItems.Any(x => !ReleaseCategories.All.Contains(x.Category)))
        {
            ModelState.AddModelError(nameof(m.Items), "An item has an invalid category.");
            await LoadPrograms();
            return View(m);
        }
        if (submittedItems.Any(x => string.IsNullOrWhiteSpace(x.ProgramVersion)))
        {
            ModelState.AddModelError(nameof(m.Items), "Enter a program version for each release item.");
            await LoadPrograms();
            return View(m);
        }

        // Remove items no longer submitted
        var submittedIds = submittedItems
            .Where(x => x.Id > 0)
            .Select(x => x.Id)
            .ToList();

        var deletedItems = e.Items
            .Where(x => !submittedIds.Contains(x.Id))
            .ToList();

        foreach (var oldItem in deletedItems)
        {
            e.Items.Remove(oldItem);
            _db.Remove(oldItem);
        }

        // Update existing items or insert new items
        var currentUserId = await GetCurrentUserIdAsync();
        foreach (var i in submittedItems)
        {
            ReleaseItem? item;

            if (i.Id > 0)
            {
                item = e.Items.FirstOrDefault(x => x.Id == i.Id);

                // Ignore IDs that do not belong to this release
                if (item == null)
                    continue;
            }
            else
            {
                item = new ReleaseItem { UserId = currentUserId, CreatedAt = DateTime.UtcNow };
                e.Items.Add(item);
            }

            item.Category = i.Category;
            item.ProgramVersion = i.ProgramVersion.Trim();
            item.ReferenceNo = i.Category == ReleaseCategories.REVISI ? null : i.ReferenceNo;
            item.Description = i.Description;
            item.ComplaintSource = i.Category == ReleaseCategories.REVISI ? i.ComplaintSource : null;
            item.SortOrder = i.SortOrder;

        }

        await _db.SaveChangesAsync();

        RefreshReleaseStatus(e);
        await _db.SaveChangesAsync();

        await _audit.LogAsync(
            "EDIT_RELEASE",
            "Release",
            e.Id,
            e.Version
        );

        return RedirectToAction(
            nameof(Details),
            new { id = e.Id }
        );
    }

    // =========================================================
    // GENERATE ZIP
    // =========================================================

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> GenerateZip(int id)
    {
        try
        {
            await _release.CreateZipAsync(id);

            TempData["Success"] = "ZIP generated successfully.";
        }
        catch (Exception ex)
        {
            TempData["Error"] = ex.Message;
        }

        return RedirectToAction(nameof(Details), new { id });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> RunReleaseActions(int id)
    {
        if (!_progress.Start(id))
            return Conflict(new { error = "Release actions are already running for this release." });
        var cancellationToken = _progress.GetCancellationToken(id);
        try
        {
            _progress.Report(id, 5, "Loading", "Loading release and program configuration.");
            var release = await Get(id);
            if (release == null)
            {
                _progress.Fail(id, "Release was not found.");
                return NotFound();
            }
            release.ReleaseDate = DateTime.Today;

            _progress.Report(id, 12, "Checking", "Checking FTP configuration and release data.");
            var ftpConfig = release.ProgramInfo.FtpConfig
                ?? throw new InvalidOperationException("FTP configuration is missing.");
            cancellationToken.ThrowIfCancellationRequested();
            var extensionFilter = string.IsNullOrWhiteSpace(release.ProgramInfo.ZipFileExtensions)
                ? "all file types"
                : release.ProgramInfo.ZipFileExtensions;
            _progress.Report(id, 18, "Packaging", $"Creating the ZIP from the program source folder ({extensionFilter}).");
            var lastZipPercent = -5;
            var zipProgress = new InlineProgress<(int Percent, int FilesProcessed, int TotalFiles)>(p =>
            {
                if (p.Percent < 100 && p.Percent - lastZipPercent < 5) return;
                lastZipPercent = p.Percent;
                _progress.Report(id, 18 + (int)(p.Percent * 0.4), "Packaging",
                    $"ZIP creation is {p.Percent}% complete; {p.FilesProcessed} of {p.TotalFiles} files finished.");
            });
            await _release.CreateZipAsync(id, zipProgress, cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            _progress.Report(id, 58, "Packaging", "ZIP created successfully.");
            release = await Get(id) ?? throw new InvalidOperationException("Release not found after ZIP generation.");
            var local = _release.GetZipPath(release);
            if (!System.IO.File.Exists(local)) throw new FileNotFoundException("Generated ZIP not found.");

            _progress.Report(id, 62, "Uploading", $"Connecting to FTP and uploading {release.ZipFileName}.");
            var remote = FtpService.CombineRemote(ftpConfig.RemoteRoot, release.ProgramInfo.FtpFolder ?? "", release.ZipFileName!);
            var lastFtpPercent = -5;
            var ftpProgress = new InlineProgress<FtpProgress>(p =>
            {
                if (p.Progress < 0 || (p.Progress < 100 && p.Progress - lastFtpPercent < 5)) return;
                lastFtpPercent = (int)p.Progress;
                _progress.Report(id, 62 + (int)(p.Progress * 0.26), "Uploading",
                    $"FTP transfer is {p.Progress:0}% complete ({p.TransferredBytes:N0} bytes sent).");
            });
            await _ftp.UploadAsync(ftpConfig, local, remote, ftpProgress, cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            _progress.Report(id, 88, "Uploading", "FTP upload completed successfully.");

            release.FtpLocation = FtpService.BuildLocation(ftpConfig, remote);
            release.UploadedAt = DateTime.UtcNow;
            release.UpdatedAt = DateTime.UtcNow;
            await _db.SaveChangesAsync();
            await _audit.LogAsync("FTP_UPLOAD", "Release", id, remote);

            _progress.Report(id, 94, "Email", "Preparing the Gmail compose window.");
            var emailConfig = release.ProgramInfo.EmailConfig;
            var to = release.ProgramInfo.EmailTo ?? emailConfig?.DefaultTo;
            var cc = release.ProgramInfo.EmailCc ?? emailConfig?.DefaultCc;
            release.UpdatedAt = DateTime.UtcNow;
            RefreshReleaseStatus(release);
            await _db.SaveChangesAsync();
            await _audit.LogAsync("OPEN_GMAIL_COMPOSE", "Release", id, release.ZipFileName);
            var gmailUrl = GmailComposeUrl(release, to, cc);
            _progress.Complete(id, "Release preparation finished. Open Gmail when you are ready.");
            return Json(new { gmailUrl });
        }
        catch (OperationCanceledException) when (_progress.IsCancelRequested(id))
        {
            _logger.LogInformation("Release {ReleaseId} was canceled by the user.", id);
            _progress.Canceled(id, "Release action canceled. Any completed ZIP or FTP upload is preserved.");
            return StatusCode(StatusCodes.Status409Conflict, new { canceled = true });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Release {ReleaseId} failed during release actions.", id);
            _progress.Fail(id, ex.Message);
            return StatusCode(StatusCodes.Status500InternalServerError, new { error = ex.Message });
        }
    }

    [HttpGet]
    public IActionResult ActionProgress(int id)
    {
        var snapshot = _progress.Get(id);
        return snapshot == null ? NotFound() : Json(snapshot);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public IActionResult CancelReleaseActions(int id)
    {
        if (!_progress.RequestCancel(id))
            return Conflict(new { error = "There is no running release action to cancel." });
        return Accepted(new { cancellationRequested = true });
    }

    // =========================================================
    // DOWNLOAD ZIP
    // =========================================================

    public async Task<IActionResult> DownloadZip(int id)
    {
        var e = await Get(id);

        if (e == null || string.IsNullOrWhiteSpace(e.ZipFileName))
            return NotFound();

        var p = _release.GetZipPath(e);

        if (!System.IO.File.Exists(p))
            return NotFound();

        return PhysicalFile(
            p,
            "application/zip",
            e.ZipFileName
        );
    }

    // =========================================================
    // FTP UPLOAD
    // =========================================================

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> UploadFtp(int id)
    {
        var e = await Get(id);

        if (e == null)
            return NotFound();

        try
        {
            if (e.ProgramInfo.FtpConfig == null)
                throw new InvalidOperationException(
                    "FTP configuration is missing."
                );

            if (string.IsNullOrWhiteSpace(e.ZipFileName))
                throw new InvalidOperationException(
                    "Generate ZIP first."
                );

            var local = _release.GetZipPath(e);

            if (!System.IO.File.Exists(local))
                throw new FileNotFoundException("ZIP not found.");

            var remote = FtpService.CombineRemote(
                e.ProgramInfo.FtpConfig.RemoteRoot,
                e.ProgramInfo.FtpFolder ?? "",
                e.ZipFileName
            );

            await _ftp.UploadAsync(
                e.ProgramInfo.FtpConfig,
                local,
                remote
            );

            e.FtpLocation = FtpService.BuildLocation(e.ProgramInfo.FtpConfig, remote);

            e.UploadedAt = DateTime.UtcNow;
            e.UpdatedAt = DateTime.UtcNow;
            RefreshReleaseStatus(e);

            await _db.SaveChangesAsync();

            await _audit.LogAsync(
                "FTP_UPLOAD",
                "Release",
                id,
                remote
            );

            TempData["Success"] = "FTP upload successful.";
        }
        catch (Exception ex)
        {
            TempData["Error"] = ex.Message;
        }

        return RedirectToAction(nameof(Details), new { id });
    }

    // =========================================================
    // EMAIL - GET
    // =========================================================

    public async Task<IActionResult> Email(int id)
    {
        var e = await Get(id);

        if (e == null)
            return NotFound();

        ViewBag.Body = _release.BuildEmail(e, CurrentDisplayName);
        ViewBag.SenderName = CurrentDisplayName;

        ViewBag.Subject = $"{e.ProgramInfo.Name} v{e.Version}";

        return View(e);
    }

    // =========================================================
    // OPEN PREFILLED GMAIL DRAFT
    // =========================================================

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> CreateGmailDraft(int id)
    {
        var e = await Get(id);

        if (e == null)
            return NotFound();

        try
        {
            var emailConfig = e.ProgramInfo.EmailConfig;
            var to = e.ProgramInfo.EmailTo ?? emailConfig?.DefaultTo;
            var cc = e.ProgramInfo.EmailCc ?? emailConfig?.DefaultCc;
            e.UpdatedAt = DateTime.UtcNow;
            await _db.SaveChangesAsync();
            await _audit.LogAsync("OPEN_GMAIL_COMPOSE", "Release", id);
            TempData["Success"] = "Gmail compose opened. Review the draft and attach the ZIP before sending.";
            return Redirect(GmailComposeUrl(e, to, cc));
        }
        catch (Exception ex)
        {
            TempData["Error"] = ex.Message;
        }

        return RedirectToAction(nameof(Email), new { id });
    }

    // =========================================================
    // WHATSAPP
    // =========================================================

    public async Task<IActionResult> WhatsApp(int id)
    {
        var e = await Get(id);

        if (e == null)
            return NotFound();

        var text = Uri.EscapeDataString(
            _release.BuildEmail(e, CurrentDisplayName)
        );

        await _audit.LogAsync("OPEN_WHATSAPP", "Release", id);

        return Redirect("https://wa.me/?text=" + text);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> MarkWhatsAppSent(int id)
    {
        var e = await _db.Releases
            .Include(x => x.ProgramInfo)
            .FirstOrDefaultAsync(x => x.Id == id);
        if (e == null) return NotFound();
        TempData["Error"] = "WhatsApp completion must be confirmed after sending the message.";
        return RedirectToAction(nameof(Details), new { id });
    }

    // =========================================================
    // QA APPROVAL
    // =========================================================

    [Authorize(Roles = "Admin,QA")]
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Approve(int id)
    {
        var e = await _db.Releases.Include(x => x.ProgramInfo).FirstOrDefaultAsync(x => x.Id == id);

        if (e == null)
            return NotFound();

        var zipExists = ZipFileExists(e);
        var missingChecklist = new List<string>();
        if (!zipExists) missingChecklist.Add("Generate ZIP");
        if (!e.UploadedAt.HasValue) missingChecklist.Add("Upload FTP");
        if (!e.EmailPreparedAt.HasValue) missingChecklist.Add("Generate email");
        if (!e.WhatsAppSentAt.HasValue) missingChecklist.Add("Send WhatsApp");
        if (missingChecklist.Count > 0)
        {
            TempData["Error"] = "Complete the release checklist before approval: " + string.Join(", ", missingChecklist) + ".";
            return RedirectToAction(nameof(Details), new { id });
        }

        e.Status = ReleaseStatus.FromVersionAndChecklist(e.Version, checklistComplete: true);
        e.QaApprovedAt = DateTime.UtcNow;
        e.UpdatedAt = DateTime.UtcNow;

        await _db.SaveChangesAsync();

        await _audit.LogAsync(
            "QA_STATUS_CONFIRMED",
            "Release",
            id,
            e.Status
        );

        return RedirectToAction(nameof(Details), new { id });
    }

    // =========================================================
    // GET RELEASE WITH RELATED DATA
    // =========================================================

    private async Task<Release?> Get(int id)
    {
        return await _db.Releases
            .Include(x => x.ProgramInfo)
                .ThenInclude(x => x.FtpConfig)
            .Include(x => x.ProgramInfo)
                .ThenInclude(x => x.EmailConfig)
            .Include(x => x.Items)
                .ThenInclude(x => x.User)
            .FirstOrDefaultAsync(x => x.Id == id);
    }

    private bool IsReleaseChecklistComplete(Release release)
    {
        return ZipFileExists(release) && release.UploadedAt.HasValue &&
               release.EmailPreparedAt.HasValue && release.WhatsAppSentAt.HasValue;
    }

    private bool ZipFileExists(Release release)
    {
        if (!release.ZipCreatedAt.HasValue || string.IsNullOrWhiteSpace(release.ZipFileName))
            return false;
        try
        {
            return System.IO.File.Exists(_release.GetZipPath(release));
        }
        catch (InvalidOperationException)
        {
            return false;
        }
    }

    private void RefreshReleaseStatus(Release release)
    {
        var complete = IsReleaseChecklistComplete(release);
        if (!complete && release.Status == ReleaseStatus.READY_QA)
            return; // READY QA is the status users may set manually while work is in progress.

        var status = ReleaseStatus.FromVersionAndChecklist(release.Version, complete);
        if (release.Status == status)
            return;

        release.Status = status;
        release.UpdatedAt = DateTime.UtcNow;
    }

    private string GmailComposeUrl(Release release, string? to, string? cc)
    {
        var subject = $"{release.ProgramInfo.Name} v{release.Version}";
        var body = _release.BuildEmail(release, CurrentDisplayName);
        return "https://mail.google.com/mail/?view=cm&fs=1" +
               "&to=" + Uri.EscapeDataString(NormalizeEmailList(to)) +
               "&cc=" + Uri.EscapeDataString(NormalizeEmailList(cc)) +
               "&su=" + Uri.EscapeDataString(subject) +
               "&body=" + Uri.EscapeDataString(body);
    }

    private static string NormalizeEmailList(string? addresses) =>
        string.Join(",", (addresses ?? "").Split(new[] { ',', ';', '\n', '\r' },
            StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));

    private sealed class InlineProgress<T>(Action<T> report) : IProgress<T>
    {
        public void Report(T value) => report(value);
    }

    // =========================================================
    // LOAD PROGRAMS
    // =========================================================

    private async Task LoadPrograms()
    {
        ViewBag.Programs = await _db.Programs
            .Where(x => x.Active)
            .OrderBy(x => x.Name)
            .ToListAsync();
    }

    // =========================================================
    // CONVERT ENTITY TO VIEW MODEL
    // =========================================================

    private static ReleaseFormViewModel ToVm(Release e)
    {
        return new ReleaseFormViewModel
        {
            Id = e.Id,
            ProgramInfoId = e.ProgramInfoId,
            Version = e.Version,
            ReleaseDate = e.ReleaseDate,
            CreatedAt = e.CreatedAt,
            LastUpdatedAt = e.Items.Select(x => (DateTime?)x.CreatedAt).Max(),
            Pic = e.Pic,
            Notes = e.Notes,
            Status = e.Status,

            Items = e.Items
                .OrderBy(x => x.SortOrder)
                .Select(x => new ReleaseItemForm
                {
                    Id = x.Id,
                    Category = x.Category,
                    ProgramVersion = x.ProgramVersion,
                    ReferenceNo = x.ReferenceNo,
                    Description = x.Description,
                    ComplaintSource = x.ComplaintSource,
                    SortOrder = x.SortOrder,
                    CreatedAt = x.CreatedAt,
                    UserDisplayName = x.User?.DisplayName
                })
                .ToList()
        };
    }

    private async Task<int?> GetCurrentUserIdAsync()
    {
        var idClaim = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (int.TryParse(idClaim, out var id)) return id;

        var username = User.Identity?.Name;
        if (string.IsNullOrWhiteSpace(username)) return null;
        return await _db.Users.Where(x => x.Username == username)
            .Select(x => (int?)x.Id)
            .FirstOrDefaultAsync();
    }

    private string CurrentDisplayName =>
        User.FindFirst("DisplayName")?.Value ?? User.Identity?.Name ?? string.Empty;

}
