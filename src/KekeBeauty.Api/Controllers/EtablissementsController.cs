using KekeBeauty.Application.Directory;
using Microsoft.AspNetCore.Mvc;

namespace KekeBeauty.Api.Controllers;

[ApiController]
[Route("etablissements")]
public sealed class EtablissementsController : ControllerBase
{
    private readonly SearchEtablissementsUseCase _searchUseCase;
    private readonly GetEtablissementDetailUseCase _detailUseCase;

    public EtablissementsController(SearchEtablissementsUseCase searchUseCase, GetEtablissementDetailUseCase detailUseCase)
    {
        _searchUseCase = searchUseCase;
        _detailUseCase = detailUseCase;
    }

    [HttpGet]
    public async Task<IActionResult> Search([FromQuery] string? categorie, [FromQuery] string? commune, CancellationToken cancellationToken)
    {
        var results = await _searchUseCase.ExecuteAsync(categorie, commune, cancellationToken);
        return Ok(results);
    }

    [HttpGet("{id:guid}")]
    public async Task<IActionResult> GetById(Guid id, CancellationToken cancellationToken)
    {
        // FR-007 : meme reponse (404) pour "inexistant" et "non valide" - pas de fuite d'information.
        var detail = await _detailUseCase.ExecuteAsync(id, cancellationToken);
        return detail is null ? NotFound() : Ok(detail);
    }
}
