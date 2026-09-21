using KekeBeauty.Application.Notifications;
using Microsoft.AspNetCore.Mvc;

namespace KekeBeauty.Api.Controllers;

[ApiController]
[Route("notifications")]
public sealed class NotificationController : ControllerBase
{
    private readonly NotificationCenterUseCase _useCase;
    private readonly KekeBeauty.Api.Auth.PartnerSessionTokenService _partnerTokens;

    public NotificationController(NotificationCenterUseCase useCase, KekeBeauty.Api.Auth.PartnerSessionTokenService partnerTokens)
    {
        _useCase = useCase;
        _partnerTokens = partnerTokens;
    }

    [HttpGet]
    public async Task<IActionResult> Lister(CancellationToken cancellationToken)
    {
        if (!TryGetIdUtilisateur(out var idUtilisateur))
        {
            return Unauthorized(new { status = "unauthorized" });
        }

        return Ok(await _useCase.ListerAsync(idUtilisateur, cancellationToken));
    }

    [HttpGet("compteur")]
    public async Task<IActionResult> Compteur(CancellationToken cancellationToken)
    {
        if (!TryGetIdUtilisateur(out var idUtilisateur))
        {
            return Unauthorized(new { status = "unauthorized" });
        }

        return Ok(new { nonLues = await _useCase.CompterNonLuesAsync(idUtilisateur, cancellationToken) });
    }

    [HttpPost("lues")]
    public async Task<IActionResult> MarquerLues(CancellationToken cancellationToken)
    {
        if (!TryGetIdUtilisateur(out var idUtilisateur))
        {
            return Unauthorized(new { status = "unauthorized" });
        }

        await _useCase.MarquerToutesLuesAsync(idUtilisateur, cancellationToken);
        return Ok(new { status = "lues" });
    }

    // Feature 018 : notifications communes a tous les types de compte (X-Client-Id reste le nom de
    // l'en-tete historique - voir RdvController - mais porte generiquement l'id_utilisateur ici).
    private bool TryGetIdUtilisateur(out Guid idUtilisateur)
    {
        idUtilisateur = Guid.Empty;
        if (Request.Headers.TryGetValue("X-Client-Id", out var clientHeader) && Guid.TryParse(clientHeader, out var idClient))
        {
            idUtilisateur = idClient;
            return true;
        }

        if (_partnerTokens.TryFromRequest(Request, out var idPartner))
        {
            idUtilisateur = idPartner;
            return true;
        }

        return false;
    }
}
