using KekeBeauty.Modules.Identity.Application;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace KekeBeauty.Modules.Identity.Infrastructure;

/// <summary>
/// Development e-mail sender: writes the message (including confirmation/reset links) to the log.
/// In Production it logs only the fact that nothing was sent, so tokens never reach production logs.
/// </summary>
public sealed class LoggingEmailSender(ILogger<LoggingEmailSender> logger, IHostEnvironment environment) : IEmailSender
{
    public Task SendAsync(EmailMessage message, CancellationToken cancellationToken = default)
    {
        if (environment.IsProduction())
        {
            logger.LogWarning("E-mail '{Subject}' to {Recipient} was not sent: no e-mail provider is configured.", message.Subject, message.To);
        }
        else
        {
            logger.LogInformation("E-mail to {Recipient} | {Subject}{NewLine}{Body}", message.To, message.Subject, Environment.NewLine, message.TextBody);
        }

        return Task.CompletedTask;
    }
}
