using System.Net;
using System.Net.Mail;
using ReleaseManager.Models;

namespace ReleaseManager.Services;

public class EmailService
{
    private readonly SecretService _secrets;
    public EmailService(SecretService secrets) => _secrets = secrets;

    public async Task SendAsync(EmailConfig config, string to, string? cc, string subject, string body, string? attachmentPath, string? senderDisplayName = null)
    {
        using var message = new MailMessage();
        var fromName = string.IsNullOrWhiteSpace(senderDisplayName) ? config.FromName ?? "" : senderDisplayName;
        message.From = new MailAddress(config.FromAddress, fromName);
        foreach (var x in Split(to)) message.To.Add(x);
        foreach (var x in Split(cc)) message.CC.Add(x);
        message.Subject = subject; message.Body = body; message.IsBodyHtml = false;
        if (!string.IsNullOrWhiteSpace(attachmentPath) && File.Exists(attachmentPath)) message.Attachments.Add(new Attachment(attachmentPath));
        using var smtp = new SmtpClient(config.SmtpHost, config.SmtpPort)
        {
            EnableSsl = config.UseSsl,
            DeliveryMethod = SmtpDeliveryMethod.Network
        };
        if (!string.IsNullOrWhiteSpace(config.Username)) smtp.Credentials = new NetworkCredential(config.Username, _secrets.Unprotect(config.PasswordEncrypted ?? ""));
        await smtp.SendMailAsync(message);
    }
    private static IEnumerable<string> Split(string? value) => (value ?? "").Split(new[] { ';', ',', '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
}
