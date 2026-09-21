using System.Net;
using System.Net.Mail;
using KekeBeauty.Application.Auth;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace KekeBeauty.Infrastructure.Auth;

/// <summary>
/// Envoie un email via SMTP. Si les identifiants SMTP ne sont pas configures, retourne un echec
/// explicite immediatement (meme garantie "jamais de fausse confirmation" que ZavuWhatsAppOtpSender
/// et WinPayerGateway - voir specs/003-auth-client/research.md, Decision 5).
/// </summary>
public sealed class SmtpEmailSender : IEmailSender
{
    private readonly string? _host;
    private readonly int _port;
    private readonly string? _user;
    private readonly string? _password;
    private readonly string _from;
    private readonly bool _enableSsl;
    private readonly ILogger<SmtpEmailSender> _logger;

    public SmtpEmailSender(IConfiguration configuration, ILogger<SmtpEmailSender> logger)
    {
        _host = configuration["Email:Smtp:Host"];
        _port = int.TryParse(configuration["Email:Smtp:Port"], out var port) ? port : 587;
        _user = configuration["Email:Smtp:User"];
        _password = configuration["Email:Smtp:Password"];
        _from = configuration["Email:Smtp:From"] ?? "no-reply@kekebeauty.local";
        _enableSsl = !bool.TryParse(configuration["Email:Smtp:DisableSsl"], out var disableSsl) || !disableSsl;
        _logger = logger;
    }

    public async Task<bool> SendAsync(string toEmail, string subject, string body, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(_host) || string.IsNullOrWhiteSpace(_user) || string.IsNullOrWhiteSpace(_password))
        {
            _logger.LogWarning("Envoi email ignore : SMTP non configure (projet Keke Beauty en cours de configuration).");
            return false;
        }

        try
        {
            using var client = new SmtpClient(_host, _port)
            {
                Credentials = new NetworkCredential(_user, _password),
                EnableSsl = _enableSsl,
            };
            using var message = new MailMessage(_from, toEmail, subject, body);
            await client.SendMailAsync(message, cancellationToken);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Envoi email echoue (exception SMTP).");
            return false;
        }
    }
}
