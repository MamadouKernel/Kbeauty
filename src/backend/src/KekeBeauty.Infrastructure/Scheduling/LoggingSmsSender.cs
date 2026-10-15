using KekeBeauty.SharedKernel;
using Microsoft.Extensions.Logging;

namespace KekeBeauty.Infrastructure.Scheduling;

/// <summary>Development SMS gateway: writes the message to the log instead of sending it (PRD §10.1 "Fake").</summary>
public sealed class LoggingSmsSender(ILogger<LoggingSmsSender> logger) : ISmsSender
{
    public Task<SmsResult> SendAsync(string phone, string message, CancellationToken cancellationToken = default)
    {
        var masked = phone.Length > 4 ? new string('*', phone.Length - 4) + phone[^4..] : phone;
        logger.LogInformation("SMS to {Phone}: {Message}", masked, message);
        return Task.FromResult(new SmsResult(true, $"log-{Guid.NewGuid():N}"));
    }
}
