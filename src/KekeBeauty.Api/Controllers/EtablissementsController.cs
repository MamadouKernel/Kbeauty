using KekeBeauty.Application.Directory;
using Microsoft.AspNetCore.Mvc;

namespace KekeBeauty.Api.Controllers;

[ApiController]
[Route("etablissements")]
public sealed class EtablissementsController : ControllerBase
{
    private readonly SearchEtablissementsUseCase _searchUseCase;
    private readonly GetEtablissementDetailUseCase _detailUseCase;
    private readonly ToggleFavoriUseCase _toggleFavoriUseCase;
    private readonly IFavoriRepository _favoriRepository;

    public EtablissementsController(
        SearchEtablissementsUseCase searchUseCase, GetEtablissementDetailUseCase detailUseCase, ToggleFavoriUseCase toggleFavoriUseCase, IFavoriRepository favoriRepository)
    {
        _searchUseCase = searchUseCase;
        _detailUseCase = detailUseCase;
        _toggleFavoriUseCase = toggleFavoriUseCase;
        _favoriRepository = favoriRepository;
    }

    [HttpGet]
    public async Task<IActionResult> Search([FromQuery] string? categorie, [FromQuery] string? commune, CancellationToken cancellationToken)
    {
        var results = await _searchUseCase.ExecuteAsync(categorie, commune, cancellationToken);
        return Ok(results);
    }

    [HttpGet("favoris")]
    public async Task<IActionResult> ListerFavoris(CancellationToken cancellationToken)
    {
        if (!Request.Headers.TryGetValue("X-Client-Id", out var header) || !Guid.TryParse(header, out var idClient))
        {
            return Unauthorized(new { status = "unauthorized" });
        }
        return Ok(await _favoriRepository.ListerAsync(idClient, cancellationToken));
    }
    [HttpGet("{id:guid}")]
    public async Task<IActionResult> GetById(Guid id, CancellationToken cancellationToken)
    {
        // X-Client-Id optionnel (endpoint public) : si present, permet de renvoyer EstFavori.
        Guid? idUtilisateurClient = Request.Headers.TryGetValue("X-Client-Id", out var header) && Guid.TryParse(header, out var parsed)
            ? parsed
            : null;

        // FR-007 : meme reponse (404) pour "inexistant" et "non valide" - pas de fuite d'information.
        var detail = await _detailUseCase.ExecuteAsync(id, idUtilisateurClient, cancellationToken);
        return detail is null ? NotFound() : Ok(detail);
    }

    [HttpPost("{id:guid}/favori")]
    public async Task<IActionResult> ToggleFavori(Guid id, CancellationToken cancellationToken)
    {
        if (!Request.Headers.TryGetValue("X-Client-Id", out var header) || !Guid.TryParse(header, out var idClient))
        {
            return Unauthorized(new { status = "unauthorized" });
        }

        var estFavori = await _toggleFavoriUseCase.ExecuteAsync(idClient, id, cancellationToken);
        return Ok(new { estFavori });
    }
}
