using KekeBeauty.Api.Auth;
using KekeBeauty.Application.Partner;
using Microsoft.AspNetCore.Mvc;

namespace KekeBeauty.Api.Controllers;

public sealed record PrestationRequest(string LibellePrestation, decimal Tarif, short DureeMinutes);
public sealed record AssignCategoryPartnerRequest(string LibelleCategorie);

[ApiController]
[Route("partenaire/etablissements/{id:guid}")]
[ServiceFilter(typeof(PartnerOwnershipFilter))]
public sealed class PartnerManagementController : ControllerBase
{
    private readonly ManagePrestationsUseCase _prestationsUseCase;
    private readonly ManageCategoriesUseCase _categoriesUseCase;

    public PartnerManagementController(ManagePrestationsUseCase prestationsUseCase, ManageCategoriesUseCase categoriesUseCase)
    {
        _prestationsUseCase = prestationsUseCase;
        _categoriesUseCase = categoriesUseCase;
    }

    [HttpPost("prestations")]
    public async Task<IActionResult> AddPrestation(Guid id, [FromBody] PrestationRequest request, CancellationToken cancellationToken)
    {
        var idPrestation = await _prestationsUseCase.AddAsync(id, request.LibellePrestation, request.Tarif, request.DureeMinutes, cancellationToken);
        return StatusCode(StatusCodes.Status201Created, new { idPrestation });
    }

    [HttpPut("prestations/{idPrestation:guid}")]
    public async Task<IActionResult> UpdatePrestation(Guid id, Guid idPrestation, [FromBody] PrestationRequest request, CancellationToken cancellationToken)
    {
        var updated = await _prestationsUseCase.UpdateAsync(id, idPrestation, request.LibellePrestation, request.Tarif, request.DureeMinutes, cancellationToken);
        return updated ? Ok(new { status = "updated" }) : NotFound();
    }

    [HttpDelete("prestations/{idPrestation:guid}")]
    public async Task<IActionResult> DeletePrestation(Guid id, Guid idPrestation, CancellationToken cancellationToken)
    {
        var deleted = await _prestationsUseCase.DeleteAsync(id, idPrestation, cancellationToken);
        return deleted ? Ok(new { status = "deleted" }) : NotFound();
    }

    [HttpPost("categories")]
    public async Task<IActionResult> AssignCategory(Guid id, [FromBody] AssignCategoryPartnerRequest request, CancellationToken cancellationToken)
    {
        var idCategorie = await _categoriesUseCase.AssignAsync(id, request.LibelleCategorie, cancellationToken);
        return Ok(new { idCategorie });
    }

    [HttpDelete("categories/{idCategorie:guid}")]
    public async Task<IActionResult> RemoveCategory(Guid id, Guid idCategorie, CancellationToken cancellationToken)
    {
        await _categoriesUseCase.RemoveAsync(id, idCategorie, cancellationToken);
        return Ok(new { status = "removed" });
    }
}
