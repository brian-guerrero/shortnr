using MailKit.Net.Smtp;
using MimeKit;

namespace Shortnr.Web.Services;

public class SmtpOptions
{
    public string Host { get; set; } = "localhost";
    public int Port { get; set; } = 1025;
}

public class EmailService(IConfiguration config, ILogger<EmailService> logger)
{
    private readonly SmtpOptions _options = config.GetSection("Smtp").Get<SmtpOptions>() ?? new();

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
