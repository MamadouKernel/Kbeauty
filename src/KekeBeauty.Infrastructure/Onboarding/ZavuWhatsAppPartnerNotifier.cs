using System.Net.Http.Json;
using KekeBeauty.Application.Onboarding;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace KekeBeauty.Infrastructure.Onboarding;

/// <summary>
/// Notifie le gerant du rejet de son dossier via WhatsApp (Zavu). Meme garantie d'echec explicite
/// que ZavuWhatsAppOtpSender (feature 003) si la cle API Zavu n'est pas configuree.
/// </summary>
public sealed class ZavuWhatsAppPartnerNotifier : IPartnerNotifier
{
    private readonly HttpClient _httpClient;
    private readonly string? _apiKey;
    private readonly ILogger<ZavuWhatsAppPartnerNotifier> _logger;

    public ZavuWhatsAppPartnerNotifier(HttpClient httpClient, IConfiguration configuration, ILogger<ZavuWhatsAppPartnerNotifier> logger)
    {
        _httpClient = httpClient;
        _apiKey = configuration["Zavu:ApiKey"];
        _logger = logger;
    }

    public async Task<bool> NotifyRejectionAsync(string telephone, string nomEtablissement, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(_apiKey))
        {
            _logger.LogWarning("Notification de rejet WhatsApp ignoree : cle API Zavu non configuree.");
            return false;
        }

        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, "/v1/messages")
            {
                Content = JsonContent.Create(new
                {
                    to = telephone,
                    channel = "whatsapp",
                    text = $"Votre dossier Keke Beauty pour '{nomEtablissement}' a ete rejete. Contactez le support pour plus d'informations."
                })
            };
            request.Headers.Add("Authorization", $"Bearer {_apiKey}");

            using var response = await _httpClient.SendAsync(request, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning("Notification de rejet WhatsApp echouee : statut HTTP {StatusCode}.", (int)response.StatusCode);
                return false;
            }

            return true;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Notification de rejet WhatsApp echouee (exception reseau/Zavu).");
            return false;
        }
    }
}
