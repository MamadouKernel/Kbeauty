using KekeBeauty.Api.Auth;
using KekeBeauty.Application.Rdv;
using Microsoft.AspNetCore.Mvc;

namespace KekeBeauty.Api.Controllers;

public sealed record RequestRdvBody(Guid IdEtablissement, Guid IdPrestation, DateTimeOffset DateHeureDebut);
public sealed record ReprogrammerBody(DateTimeOffset DateHeureDebut);

[ApiController]
public sealed class RdvController : ControllerBase
{
    private readonly RequestRdvUseCase _requestUseCase;
    private readonly DecideRdvUseCase _decideUseCase;

    public RdvController(RequestRdvUseCase requestUseCase, DecideRdvUseCase decideUseCase)
    {
        _requestUseCase = requestUseCase;
        _decideUseCase = decideUseCase;
    }

    [HttpGet("etablissements/{id:guid}/creneaux")]
    public async Task<IActionResult> GetCreneaux(Guid id, [FromQuery] DateOnly date, CancellationToken cancellationToken)
    {
        var creneaux = await _requestUseCase.GetCreneauxOccupesAsync(id, date, cancellationToken);
        return Ok(creneaux);
    }

    [HttpPost("rdv")]
    public async Task<IActionResult> RequestRdv([FromBody] RequestRdvBody body, CancellationToken cancellationToken)
    {
        if (!Request.Headers.TryGetValue("X-Client-Id", out var clientIdHeader) || !Guid.TryParse(clientIdHeader, out var idClient))
        {
            return Unauthorized(new { status = "unauthorized" });
        }

        var result = await _requestUseCase.ExecuteAsync(body.IdEtablissement, body.IdPrestation, idClient, body.DateHeureDebut, cancellationToken);

        if (!result.Success)
        {
            return result.Status == "slot_unavailable"
                ? Conflict(new { status = result.Status })
                : BadRequest(new { status = result.Status });
        }

        return StatusCode(StatusCodes.Status201Created, new { idRdv = result.IdRdv, statut = result.Status });
    }

    [HttpPost("partenaire/etablissements/{id:guid}/rdv/{idRdv:guid}/confirmer")]
    [ServiceFilter(typeof(PartnerOwnershipFilter))]
    public async Task<IActionResult> Confirmer(Guid id, Guid idRdv, CancellationToken cancellationToken)
    {
        var result = await _decideUseCase.ConfirmerAsync(id, idRdv, cancellationToken);
        return result.Success ? Ok(new { statut = result.Status }) : NotFound();
    }

    [HttpPost("partenaire/etablissements/{id:guid}/rdv/{idRdv:guid}/refuser")]
    [ServiceFilter(typeof(PartnerOwnershipFilter))]
    public async Task<IActionResult> Refuser(Guid id, Guid idRdv, CancellationToken cancellationToken)
    {
        var result = await _decideUseCase.RefuserAsync(id, idRdv, cancellationToken);
        return result.Success ? Ok(new { statut = result.Status }) : NotFound();
    }

    [HttpPost("partenaire/etablissements/{id:guid}/rdv/{idRdv:guid}/reprogrammer")]
    [ServiceFilter(typeof(PartnerOwnershipFilter))]
    public async Task<IActionResult> Reprogrammer(Guid id, Guid idRdv, [FromBody] ReprogrammerBody body, CancellationToken cancellationToken)
    {
        var result = await _decideUseCase.ReprogrammerAsync(id, idRdv, body.DateHeureDebut, cancellationToken);
        return result.Success ? Ok(new { statut = result.Status }) : Conflict(new { status = result.Status });
    }
}
