using KekeBeauty.Api.Auth;
using KekeBeauty.Application.Admin;
using Microsoft.AspNetCore.Mvc;

namespace KekeBeauty.Api.Controllers;

public sealed record ResoudreLitigeBody(string Resolution);

[ApiController]
[Route("admin/litiges")]
[ServiceFilter(typeof(AdminApiKeyFilter))]
[AdminRole("SUPPORT")]
public sealed class AdminLitigeController : ControllerBase
{
    private readonly GererLitigesUseCase _useCase;

    public AdminLitigeController(GererLitigesUseCase useCase)
    {
        _useCase = useCase;
    }

    [HttpGet]
    public async Task<IActionResult> Lister([FromQuery] string? statut, CancellationToken cancellationToken) =>
        Ok(await _useCase.ListerAsync(statut, cancellationToken));

    [HttpPost("{id:guid}/resoudre")]
    public async Task<IActionResult> Resoudre(Guid id, [FromBody] ResoudreLitigeBody body, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(body.Resolution))
        {
            return BadRequest(new { status = "resolution_requise" });
        }

        var resolved = await _useCase.ResoudreAsync(id, body.Resolution, cancellationToken);
        return resolved ? Ok(new { status = "resolu" }) : NotFound();
    }
}
