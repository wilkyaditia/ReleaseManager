
using Microsoft.EntityFrameworkCore;
using ReleaseManager.Data;
using ReleaseManager.Models;
using ReleaseManager.ViewModels;
using System;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ReleaseManager.Services
{
    public class ReleaseService
    {
        private readonly AppDbContext _db;
        private readonly AuditService _audit;
        private readonly ILogger<ReleaseService> _logger;

        public ReleaseService(
            AppDbContext db,
            AuditService audit,
            ILogger<ReleaseService> logger)
        {
            _db = db;
            _audit = audit;
            _logger = logger;
        }

        // =====================================================
        // GENERATE RELEASE ZIP
        // =====================================================

        public async Task<string> CreateZipAsync(
            int id,
            IProgress<(int Percent, int FilesProcessed, int TotalFiles)>? progress = null,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var release = await _db.Releases
                .Include(x => x.ProgramInfo)
                .Include(x => x.Items)
                .FirstOrDefaultAsync(x => x.Id == id, cancellationToken);

            if (release == null)
                throw new InvalidOperationException("Release not found.");

            if (release.ProgramInfo == null)
                throw new InvalidOperationException("Program information not found.");

            if (string.IsNullOrWhiteSpace(release.ProgramInfo.SourcePath))
                throw new InvalidOperationException("Program source path is empty.");

            if (!Directory.Exists(release.ProgramInfo.SourcePath))
                throw new DirectoryNotFoundException(
                    "Source folder not found: " + release.ProgramInfo.SourcePath);

            if (release.Items == null || !release.Items.Any())
                throw new InvalidOperationException(
                    "Add at least one release change first.");

            if (string.IsNullOrWhiteSpace(release.Version))
                throw new InvalidOperationException("Release version is required.");

            // Keep the generated ZIP beside the program source. Build it in the
            // system temp directory first so it is never picked up as an input.
            var safeProgram = SafeFileName(release.ProgramInfo.Name);
            var safeVersion = SafeFileName(release.Version);
            var dailyBaseName = $"{safeProgram}_v{safeVersion}_{DateTime.Now:yyMMdd}";
            var fileName = dailyBaseName + ".zip";
            var revision = 1;
            while (File.Exists(Path.Combine(release.ProgramInfo.SourcePath, fileName)))
                fileName = $"{dailyBaseName}_Rev {revision++}.zip";
            var path = Path.Combine(release.ProgramInfo.SourcePath, fileName);
            var tempPath = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N") + ".zip");
            var filesAdded = 0;
            var generatedPrefix = $"{safeProgram}_v{safeVersion}_";
            var allowedExtensions = (release.ProgramInfo.ZipFileExtensions ?? "")
                .Split(new[] { ',', ';', ' ', '\n', '\r', '\t' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Select(x => x.TrimStart('.'))
                .Where(x => x.Length > 0)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
            var expectedProgramFiles = allowedExtensions
                .Select(extension => $"{release.ProgramInfo.Name}.{extension}")
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
            var sourceFiles = Directory.EnumerateFiles(
                    release.ProgramInfo.SourcePath, "*", SearchOption.AllDirectories)
                .Where(sourceFile =>
                    !(Path.GetFileName(sourceFile).StartsWith(generatedPrefix, StringComparison.OrdinalIgnoreCase) &&
                      Path.GetExtension(sourceFile).Equals(".zip", StringComparison.OrdinalIgnoreCase)) &&
                    (allowedExtensions.Count == 0 || expectedProgramFiles.Contains(Path.GetFileName(sourceFile))))
                .ToList();
            var executableFiles = sourceFiles.Where(f => new[] { ".dll", ".exe" }.Contains(Path.GetExtension(f), StringComparer.OrdinalIgnoreCase)).ToList();
            if (executableFiles.Count == 0)
                throw new FileNotFoundException("No .dll or .exe file was selected for the release ZIP.");
            foreach (var executable in executableFiles)
            {
                var fileVersion = System.Diagnostics.FileVersionInfo.GetVersionInfo(executable).FileVersion;
                if (!VersionMatches(fileVersion, release.Version))
                    throw new InvalidOperationException($"Executable version mismatch: {Path.GetFileName(executable)} has version '{fileVersion}', expected '{release.Version}'.");
            }
            if (allowedExtensions.Count > 0 && sourceFiles.Count == 0)
                throw new FileNotFoundException(
                    $"No files named {string.Join(" or ", expectedProgramFiles)} were found in the program source folder.");
            var totalBytes = sourceFiles.Sum(sourceFile => new FileInfo(sourceFile).Length);
            long bytesProcessed = 0;
            var lastProgressPercent = -1;

            try
            {
                _logger.LogInformation("ZIP generation started for release {ReleaseId}. Source folder: {SourcePath}; target: {ZipPath}.",
                    id, release.ProgramInfo.SourcePath, path);
                _logger.LogInformation("ZIP extension filter for release {ReleaseId}: {Extensions}.",
                    id, allowedExtensions.Count == 0 ? "all file types" : string.Join(", ", allowedExtensions));
                if (allowedExtensions.Count > 0)
                    _logger.LogInformation("ZIP program file filter for release {ReleaseId}: {FileNames}.",
                        id, string.Join(", ", expectedProgramFiles));
                using (var archive = ZipFile.Open(tempPath, ZipArchiveMode.Create))
                {
                    foreach (var sourceFile in sourceFiles)
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        var relativePath = Path.GetRelativePath(release.ProgramInfo.SourcePath, sourceFile);
                        var entry = archive.CreateEntry(relativePath, CompressionLevel.Fastest);
                        await using var input = new FileStream(sourceFile, FileMode.Open, FileAccess.Read,
                            FileShare.Read, 81920, FileOptions.Asynchronous | FileOptions.SequentialScan);
                        await using var output = entry.Open();
                        var buffer = new byte[81920];
                        int bytesRead;
                        while ((bytesRead = await input.ReadAsync(buffer.AsMemory(0, buffer.Length), cancellationToken)) > 0)
                        {
                            await output.WriteAsync(buffer.AsMemory(0, bytesRead), cancellationToken);
                            bytesProcessed += bytesRead;
                            var percent = totalBytes > 0
                                ? (int)Math.Floor(bytesProcessed * 100d / totalBytes)
                                : (int)Math.Floor((filesAdded + 1) * 100d / Math.Max(sourceFiles.Count, 1));
                            if (percent >= lastProgressPercent + 1)
                            {
                                lastProgressPercent = percent;
                                progress?.Report((Math.Min(percent, 100), filesAdded, sourceFiles.Count));
                            }
                        }
                        filesAdded++;
                        var filePercent = totalBytes > 0
                            ? (int)Math.Floor(bytesProcessed * 100d / totalBytes)
                            : (int)Math.Floor(filesAdded * 100d / Math.Max(sourceFiles.Count, 1));
                        if (filePercent > lastProgressPercent)
                        {
                            lastProgressPercent = filePercent;
                            progress?.Report((Math.Min(filePercent, 100), filesAdded, sourceFiles.Count));
                        }
                    }
                }
                cancellationToken.ThrowIfCancellationRequested();
                File.Move(tempPath, path);
                _logger.LogInformation("ZIP generation completed for release {ReleaseId}. Added {FileCount} files to {ZipPath}.",
                    id, filesAdded, path);
            }
            catch (Exception ex)
            {
                if (ex is OperationCanceledException)
                    _logger.LogInformation("ZIP generation canceled for release {ReleaseId}.", id);
                else
                    _logger.LogError(ex, "ZIP generation failed for release {ReleaseId} from source folder {SourcePath}.",
                        id, release.ProgramInfo.SourcePath);
                throw;
            }
            finally
            {
                if (File.Exists(tempPath))
                    File.Delete(tempPath);
            }

            // Update release information
            release.ZipFileName = fileName;
            release.ZipFilePath = path;
            release.ZipCreatedAt = DateTime.UtcNow;
            release.UploadedAt = null;
            release.FtpLocation = null;
            release.EmailPreparedAt = null;
            release.WhatsAppSentAt = null;
            release.Status = ReleaseStatus.FromVersionAndChecklist(release.Version, checklistComplete: false);
            release.UpdatedAt = DateTime.UtcNow;

            await _db.SaveChangesAsync();

            // Audit log
            await _audit.LogAsync(
                "GENERATE_ZIP",
                "Release",
                id,
                fileName);

            return path;
        }


        // =====================================================
        // GET ZIP PATH
        // =====================================================

        public string GetZipPath(Release release)
        {
            if (release == null)
                throw new ArgumentNullException("release");

            if (string.IsNullOrWhiteSpace(release.ZipFileName))
                throw new InvalidOperationException(
                    "Release ZIP has not been generated.");

            if (!string.IsNullOrWhiteSpace(release.ZipFilePath))
                return release.ZipFilePath;

            if (release.ProgramInfo == null || string.IsNullOrWhiteSpace(release.ProgramInfo.SourcePath))
                throw new InvalidOperationException("Program source path is unavailable.");

            return Path.Combine(release.ProgramInfo.SourcePath, release.ZipFileName);
        }


        // =====================================================
        // BUILD RELEASE EMAIL
        // =====================================================

        public string BuildEmail(Release release, string? senderDisplayName = null)
        {
            if (release == null)
                throw new ArgumentNullException("release");

            if (release.ProgramInfo == null)
                throw new InvalidOperationException(
                    "Program information must be loaded before building email.");

            var sb = new StringBuilder();
            var items = (release.Items ?? Enumerable.Empty<ReleaseItem>())
                .OrderBy(x => x.SortOrder)
                .ThenBy(x => x.Id)
                .ToList();

            sb.AppendLine("Dear all,");
            sb.AppendLine();
            sb.AppendLine($"Program : {release.ProgramInfo.Name} v{release.Version}");
            sb.AppendLine($"Tanggal : {release.ReleaseDate:dd/MM/yyyy}");
            sb.AppendLine("Perubahan :");
            sb.AppendLine();

            var regularItems = items
                .Where(x => !string.Equals(x.Category, ReleaseCategories.REVISI, StringComparison.OrdinalIgnoreCase))
                .ToList();
            AppendChangeSection(sb, regularItems, boldHeading: true);

            var revisionItems = items
                .Where(x => string.Equals(x.Category, ReleaseCategories.REVISI, StringComparison.OrdinalIgnoreCase))
                .ToList();
            if (revisionItems.Count > 0)
            {
                sb.AppendLine("**[List Revisi]:**");
                sb.AppendLine();
                AppendNumberedChanges(sb, revisionItems, suppressReferences: true);
            }

            sb.AppendLine("**Catatan:**");
            sb.AppendLine();
            if (!string.IsNullOrWhiteSpace(release.Notes))
            {
                sb.AppendLine(release.Notes.Trim());
                sb.AppendLine();
            }
            sb.AppendLine("PIC : " + (release.Pic ?? ""));
            sb.AppendLine("Lokasi : " + (release.FtpLocation ?? ""));
            sb.AppendLine();
            sb.AppendLine("Best regard,");
            sb.AppendLine(string.IsNullOrWhiteSpace(senderDisplayName)
                ? release.CreatedBy ?? "Wilky"
                : senderDisplayName);
            return sb.ToString();
        }

        private static void AppendChangeSection(StringBuilder sb, IReadOnlyCollection<ReleaseItem> items, bool boldHeading)
        {
            if (items.Count == 0) return;

            var references = items.Select(x => x.ReferenceNo)
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            if (references.Count > 0)
            {
                var heading = string.Join(" dan ", references.Select(x => x!.Trim().TrimEnd(':')));
                sb.AppendLine(boldHeading ? $"**{heading}:**" : heading + ":");
                sb.AppendLine();
            }

            AppendNumberedChanges(sb, items, suppressReferences: true);
        }

        private static void AppendNumberedChanges(StringBuilder sb, IReadOnlyCollection<ReleaseItem> items, bool suppressReferences)
        {
            var number = 1;
            foreach (var item in items)
            {
                var description = item.Description ?? string.Empty;
                if (string.Equals(item.Category, ReleaseCategories.REVISI, StringComparison.OrdinalIgnoreCase) &&
                    !string.IsNullOrWhiteSpace(item.ComplaintSource))
                    description += $" **[Komplain {item.ComplaintSource}]**";

                if (!suppressReferences && !string.IsNullOrWhiteSpace(item.ReferenceNo))
                    description = item.ReferenceNo + ": " + description;

                sb.AppendLine($"{number}. {description}");
                number++;
            }

            sb.AppendLine();
        }


        // =====================================================
        // SAFE FILE NAME
        // =====================================================

        private static string SafeFileName(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return "Unknown";

            foreach (var c in Path.GetInvalidFileNameChars())
            {
                value = value.Replace(c, '_');
            }

            return value.Trim();
        }

        private static bool VersionMatches(string? fileVersion, string releaseVersion)
        {
            static string Normalize(string? value) => new string((value ?? "").Where(char.IsDigit).ToArray()).TrimStart('0');
            return Normalize(fileVersion) == Normalize(releaseVersion);
        }
    }
}
