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

    public PartnerDashboardController(IPartnerPrestationRepository repository)
    {
        _repository = repository;
    }

    [HttpGet("partenaire/etablissements")]
    public async Task<IActionResult> GetMesEtablissements(CancellationToken cancellationToken)
    {
        if (!Request.Headers.TryGetValue("X-Partner-Id", out var partnerIdHeader) || !Guid.TryParse(partnerIdHeader, out var idPartner))
        {
            return Unauthorized(new { status = "unauthorized" });
        }

        var etablissements = await _repository.GetEtablissementsByGerantAsync(idPartner, cancellationToken);
        return Ok(etablissements);
    }
}
