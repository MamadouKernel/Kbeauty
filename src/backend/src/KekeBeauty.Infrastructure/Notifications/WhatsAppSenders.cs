using KekeBeauty.SharedKernel;
using Microsoft.Extensions.Logging;

namespace KekeBeauty.Infrastructure.Notifications;

/// <summary>Development WhatsApp sender: logs the template name and the number of parameters (never the one-time code) instead of sending.</summary>
public sealed class LoggingWhatsAppSender(ILogger<LoggingWhatsAppSender> logger) : IWhatsAppSender
{
    public Task<WhatsAppResult> SendTemplateAsync(string phone, string template, string language, IReadOnlyList<string> parameters, bool withCodeButton, CancellationToken cancellationToken = default)
    {
        logger.LogInformation("WhatsApp template {Template} to {Phone} ({Count} parameter(s))", template, PhoneNumbers.Mask(phone), parameters.Count);
        return Task.FromResult(new WhatsAppResult(true, $"log-{Guid.NewGuid():N}"));
    }
}

/// <summary>
/// Used when a WhatsApp provider is named but no connector exists for it yet (LeTexto's API documentation has no WhatsApp
/// endpoint): every send fails visibly instead of silently pretending to deliver.
/// </summary>
public sealed class UnconfiguredWhatsAppSender : IWhatsAppSender
{
    public Task<WhatsAppResult> SendTemplateAsync(string phone, string template, string language, IReadOnlyList<string> parameters, bool withCodeButton, CancellationToken cancellationToken = default) =>
        Task.FromResult(new WhatsAppResult(false, Error: "Aucun fournisseur WhatsApp n'est configuré."));
}
