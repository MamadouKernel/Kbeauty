using System.Net.Http.Json;
using KekeBeauty.Application.Rdv;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace KekeBeauty.Infrastructure.Rdv;

/// <summary>
/// Notifie le client de la decision sur son RDV via WhatsApp (Zavu). Meme garantie d'echec explicite
/// que ZavuWhatsAppOtpSender/ZavuWhatsAppPartnerNotifier si la cle API Zavu n'est pas configuree.
/// </summary>
public sealed class ZavuWhatsAppRdvNotifier : IRdvNotifier
{
    private readonly HttpClient _httpClient;
    private readonly string? _apiKey;
    private readonly ILogger<ZavuWhatsAppRdvNotifier> _logger;

    public ZavuWhatsAppRdvNotifier(HttpClient httpClient, IConfiguration configuration, ILogger<ZavuWhatsAppRdvNotifier> logger)
    {
        _httpClient = httpClient;
        _apiKey = configuration["Zavu:ApiKey"];
        _logger = logger;
    }

    public async Task<bool> NotifyDecisionAsync(string telephone, string statutRdv, DateTimeOffset dateHeureDebut, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(_apiKey))
        {
            _logger.LogWarning("Notification de decision RDV ignoree : cle API Zavu non configuree.");
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
                    text = $"Votre rendez-vous du {dateHeureDebut:g} est desormais : {statutRdv}."
                })
            };
            request.Headers.Add("Authorization", $"Bearer {_apiKey}");

            using var response = await _httpClient.SendAsync(request, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning("Notification de decision RDV echouee : statut HTTP {StatusCode}.", (int)response.StatusCode);
                return false;
            }

            return true;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Notification de decision RDV echouee (exception reseau/Zavu).");
            return false;
        }
    }
}
