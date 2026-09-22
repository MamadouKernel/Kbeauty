using KekeBeauty.Application.Partner;
using Microsoft.AspNetCore.Mvc;

namespace KekeBeauty.Api.Controllers;

/// <summary>
/// Endpoint de decouverte "mes etablissements" (feature 011, frontend partenaire) - separe de
/// PartnerManagementController car celui-ci porte un filtre de classe (PartnerOwnershipFilter) qui
/// exige un {id} de route, absent ici par nature (on ne connait pas encore l'id a decouvrir).
/// </summary>
[ApiController]
public sealed class PartnerDashboardController : ControllerBase
{
    private readonly IPartnerPrestationRepository _repository;
    private readonly KekeBeauty.Api.Auth.PartnerSessionTokenService _tokens;
    private readonly ILogger<PartnerDashboardController> _logger;

    public PartnerDashboardController(IPartnerPrestationRepository repository, KekeBeauty.Api.Auth.PartnerSessionTokenService tokens, ILogger<PartnerDashboardController> logger)
    {
        _repository = repository;
        _tokens = tokens;
        _logger = logger;
    }

    [HttpGet("partenaire/etablissements")]
    public async Task<IActionResult> GetMesEtablissements(CancellationToken cancellationToken)
    {
        if (!_tokens.TryFromRequest(Request, out var idPartner))
        {
            var headerCount = Request.Headers["X-Partner-Token"].Count;
            var headerLen = headerCount > 0 ? Request.Headers["X-Partner-Token"][0]?.Length : (int?)null;
            _logger.LogWarning("DEBUG jeton partenaire refuse : headerCount={HeaderCount}, headerLen={HeaderLen}", headerCount, headerLen);
            return Unauthorized(new { status = "unauthorized" });
        }

        var etablissements = await _repository.GetEtablissementsByGerantAsync(idPartner, cancellationToken);
        return Ok(etablissements);
    }
}
