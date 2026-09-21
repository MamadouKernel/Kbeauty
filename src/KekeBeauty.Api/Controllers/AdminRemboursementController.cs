using KekeBeauty.Api.Auth;
using KekeBeauty.Application.Admin;
using Microsoft.AspNetCore.Mvc;

namespace KekeBeauty.Api.Controllers;

public sealed record TraiterRemboursementBody(string ReferenceRemboursement);

[ApiController]
[Route("admin/remboursements")]
[ServiceFilter(typeof(AdminApiKeyFilter))]
[AdminRole("COMPTABLE")]
public sealed class AdminRemboursementController : ControllerBase
{
    private readonly GererRemboursementsUseCase _useCase;
    public AdminRemboursementController(GererRemboursementsUseCase useCase) => _useCase = useCase;

    [HttpGet]
    public async Task<IActionResult> Lister(CancellationToken cancellationToken) => Ok(await _useCase.ListerAsync(cancellationToken));

    [HttpPost("{idRdv:guid}/traiter")]
    public async Task<IActionResult> Traiter(Guid idRdv, [FromBody] TraiterRemboursementBody body, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(body.ReferenceRemboursement)) return BadRequest(new { status = "reference_requise" });
        var traite = await _useCase.TraiterAsync(idRdv, body.ReferenceRemboursement, cancellationToken);
        return traite ? Ok(new { status = "rembourse" }) : NotFound();
    }

    [HttpPost("{idRdv:guid}/executer-wave")]
    public async Task<IActionResult> ExecuterWave(Guid idRdv, CancellationToken cancellationToken)
    {
        var result = await _useCase.ExecuterWaveAsync(idRdv, cancellationToken);
        return result.Success ? Ok(new { status = result.Status, reference = result.Reference })
            : result.Status == "refund_not_found" ? NotFound(new { status = result.Status })
            : StatusCode(StatusCodes.Status422UnprocessableEntity, new { status = result.Status, erreur = result.Error });
    }
}