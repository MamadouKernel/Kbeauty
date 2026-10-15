using KekeBeauty.Modules.Identity.Application;
using MailKit;
using MailKit.Net.Smtp;
using MailKit.Security;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MimeKit;

namespace KekeBeauty.Infrastructure.Notifications;

public sealed class SmtpOptions
{
    public const string SectionName = "Email:Smtp";

    /// <summary>Empty keeps the log-only sender (development).</summary>
    public string Host { get; init; } = string.Empty;

    public int Port { get; init; } = 587;

    /// <summary><c>StartTls</c> (port 587, default), <c>SslOnConnect</c> (port 465) or <c>None</c> (local test relays only).</summary>
    public string Security { get; init; } = "StartTls";

    public string User { get; init; } = string.Empty;
    public string Password { get; init; } = string.Empty;
    public string FromAddress { get; init; } = string.Empty;
    public string FromName { get; init; } = "Keke Beauty";

    public SecureSocketOptions SocketOptions => Security.ToLowerInvariant() switch
    {
        "none" => SecureSocketOptions.None,
        "sslonconnect" or "ssl" => SecureSocketOptions.SslOnConnect,
        _ => SecureSocketOptions.StartTls,
    };
}

/// <summary>
/// Sends the account e-mails (confirmation, password reset, billing reminders) through an SMTP relay. A delivery failure is
/// logged and swallowed: the caller never learns whether an address exists, and a relay outage must not break sign-up.
/// </summary>
public sealed class SmtpEmailSender(IOptions<SmtpOptions> options, ILogger<SmtpEmailSender> logger) : IEmailSender
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(20);

    public async Task SendAsync(EmailMessage message, CancellationToken cancellationToken = default)
    {
        var settings = options.Value;
        try
        {
            var mime = new MimeMessage();
            mime.From.Add(new MailboxAddress(settings.FromName, settings.FromAddress));
            mime.To.Add(MailboxAddress.Parse(message.To));
            // MimeKit folds and encodes header values, but a subject is still reduced to one line.
            mime.Subject = message.Subject.ReplaceLineEndings(" ");
            mime.Body = new TextPart("plain") { Text = message.TextBody };

            using var client = new SmtpClient { Timeout = (int)Timeout.TotalMilliseconds };
            await client.ConnectAsync(settings.Host, settings.Port, settings.SocketOptions, cancellationToken);
            if (!string.IsNullOrEmpty(settings.User))
            {
                await client.AuthenticateAsync(settings.User, settings.Password, cancellationToken);
            }

            await client.SendAsync(mime, cancellationToken);
            await client.DisconnectAsync(true, cancellationToken);
        }
        catch (Exception ex) when (ex is ProtocolException or ServiceNotConnectedException or ServiceNotAuthenticatedException
            or CommandException or AuthenticationException or FormatException or ParseException or IOException
            or System.Net.Sockets.SocketException or TimeoutException)
        {
            logger.LogError(ex, "E-mail '{Subject}' could not be delivered to {Recipient}", message.Subject, message.To);
        }
    }
}
