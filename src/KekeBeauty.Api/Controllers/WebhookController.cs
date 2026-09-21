using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Serialization;
using KekeBeauty.Application.Billing;
using KekeBeauty.Application.Rdv;
using Microsoft.AspNetCore.Mvc;

namespace KekeBeauty.Api.Controllers;

public sealed class WiniPayerCallbackBody
{
    [JsonPropertyName("uuid")] public string? Uuid { get; set; }
    [JsonPropertyName("crypto")] public string? Crypto { get; set; }
    [JsonPropertyName("amount")] public decimal Amount { get; set; }
    [JsonPropertyName("created_at")] public string? CreatedAt { get; set; }
    [JsonPropertyName("state")] public string? State { get; set; }
    [JsonPropertyName("operator")] public string? Operator { get; set; }
    [JsonPropertyName("hash")] public string? Hash { get; set; }
}

[ApiController]
[Route("webhooks/winipayer")]
public sealed class WebhookController : ControllerBase
{
    private readonly IAbonnementRepository _repository;
    private readonly IRdvPaiementRepository _rdvPaiementRepository;
    private readonly IPourboireRepository _pourboireRepository;
    private readonly IConfiguration _configuration;
    private readonly ILogger<WebhookController> _logger;

    public WebhookController(
        IAbonnementRepository repository, IRdvPaiementRepository rdvPaiementRepository,
        IPourboireRepository pourboireRepository, IConfiguration configuration, ILogger<WebhookController> logger)
    {
        _repository = repository;
        _rdvPaiementRepository = rdvPaiementRepository;
        _pourboireRepository = pourboireRepository;
        _configuration = configuration;
        _logger = logger;
    }

    /// <summary>
    /// Notification WiniPayer du resultat d'un paiement (voir specs/008-abonnement-paiement,
    /// research.md Decision 4). Le hash recu est verifie contre sha256(privateKey + uuid + crypto
    /// + amount + created_at) pour garantir l'authenticite (doc WiniPayer "Vérifier un lien de
    /// paiement"), sinon la notification est rejetee sans effet sur l'abonnement.
    /// </summary>
    [HttpPost("callback")]
    public async Task<IActionResult> Callback([FromBody] WiniPayerCallbackBody body, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(body.Uuid) || string.IsNullOrWhiteSpace(body.Crypto) ||
            string.IsNullOrWhiteSpace(body.CreatedAt) || string.IsNullOrWhiteSpace(body.Hash))
        {
            return BadRequest(new { status = "invalid_payload" });
        }

        var env = _configuration["Billing:WiniPayer:Env"] ?? "test";
        var privateKey = env == "prod"
            ? _configuration["Billing:WiniPayer:ProdPrivateKey"]
            : _configuration["Billing:WiniPayer:TestPrivateKey"];

        if (string.IsNullOrWhiteSpace(privateKey))
        {
            _logger.LogWarning("Callback WiniPayer ignore : cle privee non configuree.");
            return StatusCode(StatusCodes.Status503ServiceUnavailable, new { status = "not_configured" });
        }

        var expectedHash = ComputeHash(privateKey, body.Uuid, body.Crypto, body.Amount, body.CreatedAt);
        if (!CryptographicOperations.FixedTimeEquals(
                Encoding.UTF8.GetBytes(expectedHash.ToUpperInvariant()), Encoding.UTF8.GetBytes(body.Hash.ToUpperInvariant())))
        {
            _logger.LogWarning("Callback WiniPayer rejete : signature invalide pour la facture {Uuid}.", body.Uuid);
            return Unauthorized(new { status = "invalid_signature" });
        }

        var paiementReussi = body.State == "success";
        var applique = await _repository.MarquerPaiementAsync(body.Uuid, paiementReussi, body.Operator, cancellationToken);

        // Feature 013 : un meme callback WiniPayer sert les deux flux (abonnement et RDV), aucun
        // moyen de les distinguer avant reception (un seul callback_url configure cote marchand -
        // voir research.md Decision 3). Si la reference n'appartenait pas a un abonnement, on
        // essaie le repository de paiement RDV ; chaque methode est idempotente et ignore les
        // references qui ne lui appartiennent pas.
        if (!applique)
        {
            applique = await _rdvPaiementRepository.MarquerPaiementAsync(body.Uuid, paiementReussi, body.Operator, cancellationToken);
        }

        if (!applique)
        {
            var pourboire = await _pourboireRepository.ObtenirParReferenceAsync(body.Uuid, cancellationToken);
            if (pourboire is not null)
            {
                await _pourboireRepository.MarquerStatutAsync(body.Uuid, paiementReussi ? "REUSSIE" : "ECHOUEE", cancellationToken);
                applique = true;
            }
        }

        // Idempotent (voir IAbonnementRepository/IRdvPaiementRepository.MarquerPaiementAsync) : un
        // callback rejoue ou deja traite renvoie simplement false, sans erreur - WiniPayer ne doit
        // pas reessayer.
        return Ok(new { status = applique ? "applied" : "already_processed_or_unknown" });
    }

    private static string ComputeHash(string privateKey, string uuid, string crypto, decimal amount, string createdAt)
    {
        var amountStr = amount == Math.Truncate(amount) ? ((long)amount).ToString() : amount.ToString();
        var raw = $"{privateKey}{uuid}{crypto}{amountStr}{createdAt}";
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(raw));
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }
}
