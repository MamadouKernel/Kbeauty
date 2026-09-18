using System.Net.Http.Json;
using KekeBeauty.Application.Billing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace KekeBeauty.Infrastructure.Billing;

/// <summary>
/// Notifie le gerant d'un abonnement impaye via WhatsApp (Zavu). Meme garantie d'echec explicite
/// que ZavuWhatsAppOtpSender/ZavuWhatsAppPartnerNotifier/ZavuWhatsAppRdvNotifier.
/// </summary>
public sealed class ZavuWhatsAppAbonnementNotifier : IAbonnementNotifier
{
    private readonly HttpClient _httpClient;
    private readonly string? _apiKey;
    private readonly ILogger<ZavuWhatsAppAbonnementNotifier> _logger;

    public ZavuWhatsAppAbonnementNotifier(HttpClient httpClient, IConfiguration configuration, ILogger<ZavuWhatsAppAbonnementNotifier> logger)
    {
        _httpClient = httpClient;
        _apiKey = configuration["Zavu:ApiKey"];
        _logger = logger;
    }

    public async Task<bool> NotifyRelanceAsync(string telephone, decimal montant, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(_apiKey))
        {
            _logger.LogWarning("Relance abonnement ignoree : cle API Zavu non configuree.");
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
                    text = $"Votre abonnement Keke Beauty est impaye ({montant} XOF). Merci de regulariser."
                })
            };
            request.Headers.Add("Authorization", $"Bearer {_apiKey}");

            using var response = await _httpClient.SendAsync(request, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning("Relance abonnement echouee : statut HTTP {StatusCode}.", (int)response.StatusCode);
                return false;
            }

            return true;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Relance abonnement echouee (exception reseau/Zavu).");
            return false;
        }
    }
}
