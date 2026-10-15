using KekeBeauty.Modules.Identity.Application;
using KekeBeauty.SharedKernel;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace KekeBeauty.Infrastructure.Notifications;

/// <summary>Sends SMS through LeTexto. The delivery-report URL is attached so that "sent" can later become "delivered" or "failed".</summary>
public sealed class LeTextoSmsSender(LeTextoClient client, IOptions<LeTextoOptions> options) : ISmsSender
{
    public async Task<SmsResult> SendAsync(string phone, string message, CancellationToken cancellationToken = default)
    {
        var result = await client.SendSmsAsync(phone, message, options.Value.DeliveryReportUrl, cancellationToken);
        return new SmsResult(result.Success, result.Id, result.Error);
    }
}

/// <summary>
/// Sends the account e-mails through LeTexto e-mail campaigns (one recipient each). Like the SMTP sender, a failure is logged
/// and swallowed: the caller never learns whether an address exists and a provider outage must not break sign-up.
/// </summary>
public sealed class LeTextoEmailSender(LeTextoClient client, ILogger<LeTextoEmailSender> logger) : IEmailSender
{
    public async Task SendAsync(EmailMessage message, CancellationToken cancellationToken = default)
    {
        try
        {
            var result = await client.SendEmailAsync(message.To, message.Subject.ReplaceLineEndings(" "), message.TextBody, cancellationToken);
            if (!result.Success)
            {
                logger.LogWarning("LeTexto refused an e-mail: {Error}", result.Error);
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "E-mail could not be sent through LeTexto");
        }
    }
}
