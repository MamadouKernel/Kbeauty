using System.Net.Http.Json;
using KekeBeauty.Application.Billing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace KekeBeauty.Infrastructure.Billing;

/// <summary>
/// Tentative de paiement via CinetPay. Meme garantie d'echec explicite que les integrations Zavu
/// (003/004/006/007) si le compte agregateur n'est pas encore configure (voir research.md Decision 1).
/// </summary>
public sealed class CinetPayGateway : IPaymentGateway
{
    private readonly HttpClient _httpClient;
    private readonly string? _apiKey;
    private readonly ILogger<CinetPayGateway> _logger;

    public CinetPayGateway(HttpClient httpClient, IConfiguration configuration, ILogger<CinetPayGateway> logger)
    {
        _httpClient = httpClient;
        _apiKey = configuration["Billing:CinetPay:ApiKey"];
        _logger = logger;
    }

    public async Task<bool> InitiateAsync(string canal, decimal montant, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(_apiKey))
        {
            _logger.LogWarning("Paiement ignore : cle API CinetPay non configuree.");
            return false;
        }

        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, "/v2/payment")
            {
                Content = JsonContent.Create(new { apikey = _apiKey, channel = canal, amount = montant, currency = "XOF" })
            };

            using var response = await _httpClient.SendAsync(request, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning("Paiement CinetPay echoue : statut HTTP {StatusCode}.", (int)response.StatusCode);
                return false;
            }

            return true;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Paiement CinetPay echoue (exception reseau/agregateur).");
            return false;
        }
    }
}
