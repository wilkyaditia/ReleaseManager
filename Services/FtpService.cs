using FluentFTP;
using ReleaseManager.Services;
using AppFtpConfig = ReleaseManager.Models.FtpConfig;

namespace ReleaseManager.Services;

public class FtpService
{
    private readonly SecretService _secrets;
    private readonly ILogger<FtpService> _logger;
    public FtpService(SecretService secrets, ILogger<FtpService> logger)
    {
        _secrets = secrets;
        _logger = logger;
    }

    public async Task UploadAsync(
        AppFtpConfig config,
        string localPath,
        string remotePath,
        IProgress<FtpProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        var password = _secrets.Unprotect(config.PasswordEncrypted ?? "");
        using var client = new AsyncFtpClient(config.Host, config.Username, password, config.Port);
        client.Config.ConnectTimeout = 20_000;
        client.Config.ReadTimeout = 30_000;
        client.Config.WriteTimeout = 60_000;
        client.Config.DataConnectionConnectTimeout = 20_000;
        client.Config.DataConnectionReadTimeout = 60_000;
        client.Config.DataConnectionWriteTimeout = 60_000;
        client.Config.RetryAttempts = 1;

        if (config.UseFtps)
        {
            client.Config.EncryptionMode = FtpEncryptionMode.Explicit;
            client.Config.ValidateAnyCertificate = !config.ValidateCertificate;
        }

        using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(30));
        using var linkedCancellation = CancellationTokenSource.CreateLinkedTokenSource(timeout.Token, cancellationToken);
        try
        {
            _logger.LogInformation("FTP upload connecting to {Host}:{Port} for {RemotePath}.", config.Host, config.Port, remotePath);
            await client.Connect(linkedCancellation.Token);
            _logger.LogInformation("FTP connected to {Host}; starting upload of {LocalPath}.", config.Host, localPath);
            var result = await client.UploadFile(
                localPath,
                remotePath,
                FtpRemoteExists.Overwrite,
                true,
                FtpVerify.None,
                progress,
                linkedCancellation.Token);

            if (result != FtpStatus.Success)
                throw new InvalidOperationException("FTP upload failed: " + result);
            _logger.LogInformation("FTP upload completed for {RemotePath} with status {Status}.", remotePath, result);
        }
        catch (OperationCanceledException) when (timeout.IsCancellationRequested && !cancellationToken.IsCancellationRequested)
        {
            _logger.LogError("FTP upload timed out for {Host} and {RemotePath}.", config.Host, remotePath);
            throw new TimeoutException("FTP upload timed out after 30 minutes. Check the FTP server and network connection.");
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            _logger.LogInformation("FTP upload canceled for {Host} and {RemotePath}.", config.Host, remotePath);
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "FTP upload failed for {Host} and {RemotePath}.", config.Host, remotePath);
            throw;
        }
    }

    public static string CombineRemote(string? root, string folder, string fileName)
    {
        var parts = new List<string>();
        if (!string.IsNullOrWhiteSpace(root)) parts.Add(root.Trim('/'));
        if (!string.IsNullOrWhiteSpace(folder)) parts.Add(folder.Trim('/'));
        parts.Add(fileName);
        return "/" + string.Join("/", parts);
    }

    public static string BuildLocation(AppFtpConfig config, string remotePath)
    {
        var scheme = config.UseFtps ? "ftpes" : "ftp";
        var username = Uri.EscapeDataString(config.Username);
        var port = config.Port is > 0 and not 21 ? $":{config.Port}" : "";
        return $"{scheme}://{username}@{config.Host}{port}{remotePath}";
    }
}
