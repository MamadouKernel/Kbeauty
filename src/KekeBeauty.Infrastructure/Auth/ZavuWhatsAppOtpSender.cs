using System.Net.Http.Json;
using KekeBeauty.Application.Auth;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace KekeBeauty.Infrastructure.Auth;

/// <summary>
/// Envoie le code OTP via WhatsApp en utilisant l'API Zavu. Si la cle API Zavu n'est pas configuree
/// (projet Zavu Keke Beauty en cours de mise en place), retourne un echec explicite immediatement,
/// sans appel reseau (FR-009, voir specs/003-auth-client/research.md, Decision 5).
/// </summary>
public sealed class ZavuWhatsAppOtpSender : IOtpSender
{
    private readonly HttpClient _httpClient;
    private readonly string? _apiKey;
    private readonly ILogger<ZavuWhatsAppOtpSender> _logger;

    public ZavuWhatsAppOtpSender(HttpClient httpClient, IConfiguration configuration, ILogger<ZavuWhatsAppOtpSender> logger)
    {
        _httpClient = httpClient;
        _apiKey = configuration["Zavu:ApiKey"];
        _logger = logger;
    }

    public async Task<bool> SendAsync(string telephone, string code, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(_apiKey))
        {
            _logger.LogWarning("Envoi OTP WhatsApp ignore : cle API Zavu non configuree (projet Keke Beauty en cours de configuration).");
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
                    text = $"Votre code Keke Beauty : {code}. Il expire dans 5 minutes."
                })
            };
            request.Headers.Add("Authorization", $"Bearer {_apiKey}");

            using var response = await _httpClient.SendAsync(request, cancellationToken);
            // Le code OTP n'est jamais journalise (FR-010) - seul le resultat (succes/echec) est logue.
            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning("Envoi OTP WhatsApp echoue : statut HTTP {StatusCode}.", (int)response.StatusCode);
                return false;
            }

            return true;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Envoi OTP WhatsApp echoue (exception reseau/Zavu).");
            return false;
        }
    }
}
