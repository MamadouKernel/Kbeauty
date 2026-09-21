using Google.Apis.Auth;
using KekeBeauty.Application.Auth;
using Microsoft.AspNetCore.Mvc;
using System.Security.Cryptography;
using System.Text;

namespace KekeBeauty.Api.Controllers;

public sealed record GoogleLoginRequest(string Credential, string? Role = null, string? DeviceTrustToken = null);
public sealed record StepUpVerifyRequest(string Ticket, string Code);

[ApiController]
[Route("auth/google")]
[Microsoft.AspNetCore.RateLimiting.EnableRateLimiting("auth")]
public sealed class GoogleAuthController : ControllerBase
{
    private readonly IUtilisateurRepository _users;
    private readonly IConfiguration _configuration;
    private readonly ILogger<GoogleAuthController> _logger;
    private readonly KekeBeauty.Api.Auth.PartnerSessionTokenService _partnerTokens;
    private readonly KekeBeauty.Api.Auth.UserSessionTokenService _userTokens;
    private readonly KekeBeauty.Api.Auth.DeviceTrustTokenService _deviceTrust;
    private readonly KekeBeauty.Api.Auth.StepUpTicketService _stepUpTickets;
    private readonly RequestStepUpUseCase _requestStepUp;
    private readonly VerifyStepUpUseCase _verifyStepUp;

    public GoogleAuthController(
        IUtilisateurRepository users, IConfiguration configuration, ILogger<GoogleAuthController> logger,
        KekeBeauty.Api.Auth.PartnerSessionTokenService partnerTokens, KekeBeauty.Api.Auth.UserSessionTokenService userTokens,
        KekeBeauty.Api.Auth.DeviceTrustTokenService deviceTrust, KekeBeauty.Api.Auth.StepUpTicketService stepUpTickets,
        RequestStepUpUseCase requestStepUp, VerifyStepUpUseCase verifyStepUp)
    {
        _users = users;
        _configuration = configuration;
        _logger = logger;
        _partnerTokens = partnerTokens;
        _userTokens = userTokens;
        _deviceTrust = deviceTrust;
        _stepUpTickets = stepUpTickets;
        _requestStepUp = requestStepUp;
        _verifyStepUp = verifyStepUp;
    }

    [HttpGet("config")]
    public IActionResult Config()
    {
        var enabled = !string.IsNullOrWhiteSpace(_configuration["Authentication:Google:ClientId"]);
        return Ok(new { enabled });
    }

    [HttpPost("login")]
    public async Task<IActionResult> Login([FromBody] GoogleLoginRequest request, CancellationToken cancellationToken)
    {
        var clientId = _configuration["Authentication:Google:ClientId"];
        if (string.IsNullOrWhiteSpace(clientId))
            return StatusCode(StatusCodes.Status503ServiceUnavailable, new { status = "google_not_configured" });
        if (string.IsNullOrWhiteSpace(request.Credential))
            return BadRequest(new { status = "invalid_google_credential" });

        try
        {
            var payload = await GoogleJsonWebSignature.ValidateAsync(request.Credential,
                new GoogleJsonWebSignature.ValidationSettings { Audience = [clientId] });
            if (string.IsNullOrWhiteSpace(payload.Subject) || payload.EmailVerified != true)
                return Unauthorized(new { status = "google_account_unverified" });

            var typeCompte = string.Equals(request.Role, "partenaire", StringComparison.OrdinalIgnoreCase)
                ? KekeBeauty.Domain.Entities.TypeCompte.Partenaire
                : KekeBeauty.Domain.Entities.TypeCompte.Client;
            var (user, isNew) = await _users.FindOrCreateGoogleAsync(
                payload.Subject, payload.Email, payload.Name ?? payload.Email ?? "Keke Beauty", typeCompte, cancellationToken);
            if (user.EstSuspendu)
                return StatusCode(StatusCodes.Status403Forbidden, new { status = "account_suspended" });

            var typeComptePourJeton = typeCompte == KekeBeauty.Domain.Entities.TypeCompte.Partenaire ? "PARTENAIRE" : "CLIENT";

            // 2FA "par etape" : uniquement pour un compte deja existant connecte depuis un appareil
            // non reconnu (aucun jeton de confiance valide). Un compte tout juste cree n'a par
            // definition aucun "appareil habituel" a comparer - la verification Google suffit a la
            // creation, comme avant. Voir echange utilisateur : jamais a chaque connexion.
            if (!isNew && !_deviceTrust.IsTrusted(request.DeviceTrustToken, user.IdUtilisateur))
            {
                var stepUp = await _requestStepUp.ExecuteAsync(user.IdUtilisateur, user.Email, user.Telephone, cancellationToken);
                return Ok(new
                {
                    status = "step_up_required",
                    stepUpTicket = _stepUpTickets.Create(user.IdUtilisateur, typeComptePourJeton),
                    emailSent = stepUp.EmailSent,
                    whatsAppSent = stepUp.WhatsAppSent,
                });
            }

            string? onboardingToken = null;
            if (typeCompte == KekeBeauty.Domain.Entities.TypeCompte.Partenaire)
            {
                onboardingToken = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
                var tokenHash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(onboardingToken)));
                await _users.StorePartnerOnboardingTokenAsync(user.IdUtilisateur, tokenHash, DateTimeOffset.UtcNow.AddMinutes(30), cancellationToken);
            }
            return Ok(new
            {
                status = "verified",
                idUtilisateur = user.IdUtilisateur,
                isNewAccount = isNew,
                onboardingToken,
                sessionToken = typeCompte == KekeBeauty.Domain.Entities.TypeCompte.Partenaire ? _partnerTokens.Create(user.IdUtilisateur) : _userTokens.Create(user.IdUtilisateur, "CLIENT"),
                deviceTrustToken = _deviceTrust.Create(user.IdUtilisateur),
            });
        }
        catch (InvalidJwtException ex)
        {
            _logger.LogInformation(ex, "Jeton Google refuse.");
            return Unauthorized(new { status = "invalid_google_credential" });
        }
    }

    [HttpPost("step-up/verify")]
    public async Task<IActionResult> VerifyStepUp([FromBody] StepUpVerifyRequest request, CancellationToken cancellationToken)
    {
        if (!_stepUpTickets.TryValidate(request.Ticket, out var userId, out var typeCompteStr))
            return Unauthorized(new { status = "invalid_ticket" });
        if (string.IsNullOrWhiteSpace(request.Code) || !await _verifyStepUp.ExecuteAsync(userId, request.Code, cancellationToken))
            return BadRequest(new { status = "invalid_code" });

        var isPartner = typeCompteStr == "PARTENAIRE";
        return Ok(new
        {
            status = "verified",
            idUtilisateur = userId,
            sessionToken = isPartner ? _partnerTokens.Create(userId) : _userTokens.Create(userId, "CLIENT"),
            deviceTrustToken = _deviceTrust.Create(userId),
        });
    }
}
