using System.Net.Http.Json;
using System.Text.Json.Serialization;
using KekeBeauty.Application.Billing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace KekeBeauty.Infrastructure.Billing;

/// <summary>
/// Genere un lien de paiement heberge via l'API WiniPayer (Standard checkout). Meme garantie
/// d'echec explicite que les autres integrations (Zavu, ex-CinetPay) si le compte marchand n'est
/// pas configure. A la difference de CinetPay, WiniPayer ne rend pas un resultat de paiement
/// synchrone : le client est redirige vers CheckoutUrl, et le resultat arrive via callback
/// (voir specs/008-abonnement-paiement/research.md Decision 1 - mise a jour WiniPayer).
/// </summary>
public sealed class WinPayerGateway : IPaymentGateway
{
    private sealed class CreateResponse
    {
        [JsonPropertyName("success")] public bool Success { get; set; }
        [JsonPropertyName("results")] public CreateResults? Results { get; set; }
    }

    private sealed class CreateResults
    {
        [JsonPropertyName("uuid")] public string? Uuid { get; set; }
        [JsonPropertyName("checkout_process")] public string? CheckoutProcess { get; set; }
    }

    private readonly HttpClient _httpClient;
    private readonly string? _merchantApply;
    private readonly string? _tokenKey;
    private readonly string _env;
    private readonly string _publicBaseUrl;
    private readonly ILogger<WinPayerGateway> _logger;

    public WinPayerGateway(HttpClient httpClient, IConfiguration configuration, ILogger<WinPayerGateway> logger)
    {
        _httpClient = httpClient;
        _env = configuration["Billing:WiniPayer:Env"] ?? "test";
        _merchantApply = configuration["Billing:WiniPayer:MerchantApply"];
        _tokenKey = _env == "prod"
            ? configuration["Billing:WiniPayer:ProdTokenKey"]
            : configuration["Billing:WiniPayer:TestTokenKey"];
        _publicBaseUrl = configuration["Billing:WiniPayer:PublicBaseUrl"] ?? "http://localhost:5080";
        _logger = logger;
    }

    public async Task<PaymentInitiation> InitiateAsync(decimal montant, string description, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(_merchantApply) || string.IsNullOrWhiteSpace(_tokenKey))
        {
            _logger.LogWarning("Souscription ignoree : compte marchand WiniPayer non configure.");
            return new PaymentInitiation { Success = false };
        }

        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, "/checkout/standard/create")
            {
                Content = new FormUrlEncodedContent(new Dictionary<string, string>
                {
                    ["env"] = _env,
                    ["amount"] = ((int)montant).ToString(),
                    ["description"] = description,
                    ["cancel_url"] = $"{_publicBaseUrl}/billing/winipayer/cancel",
                    ["return_url"] = $"{_publicBaseUrl}/billing/winipayer/return",
                    ["callback_url"] = $"{_publicBaseUrl}/webhooks/winipayer/callback",
                })
            };
            request.Headers.Add("X-Merchant-Apply", _merchantApply);
            request.Headers.Add("X-Merchant-Token", _tokenKey);

            using var response = await _httpClient.SendAsync(request, cancellationToken);
            var body = await response.Content.ReadFromJsonAsync<CreateResponse>(cancellationToken: cancellationToken);

            if (!response.IsSuccessStatusCode || body is not { Success: true, Results: not null })
            {
                _logger.LogWarning("Creation du lien de paiement WiniPayer echouee : statut HTTP {StatusCode}.", (int)response.StatusCode);
                return new PaymentInitiation { Success = false };
            }

            return new PaymentInitiation
            {
                Success = true,
                CheckoutUrl = body.Results.CheckoutProcess,
                ReferenceExterne = body.Results.Uuid,
            };
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Creation du lien de paiement WiniPayer echouee (exception reseau/agregateur).");
            return new PaymentInitiation { Success = false };
        }
    }
}
