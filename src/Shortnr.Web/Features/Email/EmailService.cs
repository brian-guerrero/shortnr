using MailKit.Net.Smtp;
using Microsoft.Extensions.Options;
using MimeKit;

namespace Shortnr.Web.Features.Email;

public sealed class EmailService(IOptions<SmtpOptions> options, ILogger<EmailService> logger)
{
    private readonly SmtpOptions _options = options.Value;

    public async Task SendAsync(string to, string subject, string plainTextBody, CancellationToken ct = default)
    {
        var message = new MimeMessage();
        message.From.Add(new MailboxAddress("shortnr", "noreply@shortnr.local"));
        message.To.Add(MailboxAddress.Parse(to));
        message.Subject = subject;
        message.Body = new TextPart("plain") { Text = plainTextBody };

        using var client = new SmtpClient();
        await client.ConnectAsync(_options.Host, _options.Port, MailKit.Security.SecureSocketOptions.None, ct);
        await client.SendAsync(message, ct);
        await client.DisconnectAsync(true, ct);

        logger.LogInformation("Sent email to {To} with subject '{Subject}'", to, subject);
    }
}