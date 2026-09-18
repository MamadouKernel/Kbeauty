using KekeBeauty.Api.Auth;
using KekeBeauty.Application.Directory;
using KekeBeauty.Application.Onboarding;
using Microsoft.AspNetCore.Mvc;

namespace KekeBeauty.Api.Controllers;

public sealed record AssignCategoryRequest(string LibelleCategorie);
public sealed record AddPrestationRequest(string LibellePrestation, decimal Tarif, short DureeMinutes);

[ApiController]
[Route("admin/applications")]
[ServiceFilter(typeof(AdminApiKeyFilter))]
public sealed class AdminController : ControllerBase
{
    private readonly ListPendingApplicationsUseCase _listUseCase;
    private readonly ValidateApplicationUseCase _validateUseCase;
    private readonly RejectApplicationUseCase _rejectUseCase;
    private readonly IEtablissementRepository _etablissementRepository;
    private readonly IFileStorage _fileStorage;
    private readonly AssignCategoryUseCase _assignCategoryUseCase;
    private readonly AddPrestationUseCase _addPrestationUseCase;

    public AdminController(
        ListPendingApplicationsUseCase listUseCase,
        ValidateApplicationUseCase validateUseCase,
        RejectApplicationUseCase rejectUseCase,
        IEtablissementRepository etablissementRepository,
        IFileStorage fileStorage,
        AssignCategoryUseCase assignCategoryUseCase,
        AddPrestationUseCase addPrestationUseCase)
    {
        _listUseCase = listUseCase;
        _validateUseCase = validateUseCase;
        _rejectUseCase = rejectUseCase;
        _etablissementRepository = etablissementRepository;
        _fileStorage = fileStorage;
        _assignCategoryUseCase = assignCategoryUseCase;
        _addPrestationUseCase = addPrestationUseCase;
    }

    [HttpGet]
    public async Task<IActionResult> List([FromQuery] string statut, CancellationToken cancellationToken)
    {
        var applications = await _listUseCase.ExecuteAsync(statut, cancellationToken);
        return Ok(applications);
    }

    [HttpGet("{id:guid}")]
    public async Task<IActionResult> GetById(Guid id, CancellationToken cancellationToken)
    {
        var application = await _etablissementRepository.GetByIdAsync(id, cancellationToken);
        return application is null ? NotFound() : Ok(application);
    }

    [HttpGet("{id:guid}/files/{type}")]
    public async Task<IActionResult> GetFile(Guid id, string type, CancellationToken cancellationToken)
    {
        if (type != "devanture" && type != "piece-identite")
        {
            return NotFound();
        }

        var relativePath = await _etablissementRepository.GetFilePathAsync(id, type, cancellationToken);
        if (relativePath is null)
        {
            return NotFound();
        }

        var stream = await _fileStorage.OpenAsync(relativePath, cancellationToken);
        return stream is null ? NotFound() : File(stream, "application/octet-stream");
    }

    [HttpPost("{id:guid}/validate")]
    public async Task<IActionResult> Validate(Guid id, CancellationToken cancellationToken)
    {
        var result = await _validateUseCase.ExecuteAsync(id, cancellationToken);
        return result.Status switch
        {
            "not_found" => NotFound(),
            "missing_documents" => Conflict(new { status = result.Status }),
            _ => Ok(new { statut = result.Status }),
        };
    }

    [HttpPost("{id:guid}/reject")]
    public async Task<IActionResult> Reject(Guid id, CancellationToken cancellationToken)
    {
        var result = await _rejectUseCase.ExecuteAsync(id, cancellationToken);
        return result.Status == "not_found" ? NotFound() : Ok(new { statut = result.Status });
    }

    [HttpPost("{id:guid}/categories")]
    public async Task<IActionResult> AssignCategory(Guid id, [FromBody] AssignCategoryRequest request, CancellationToken cancellationToken)
    {
        var result = await _assignCategoryUseCase.ExecuteAsync(id, request.LibelleCategorie, cancellationToken);
        return result.Status == "not_found"
            ? NotFound()
            : Ok(new { idCategorie = result.IdCategorie, libelleCategorie = request.LibelleCategorie });
    }

    [HttpPost("{id:guid}/prestations")]
    public async Task<IActionResult> AddPrestation(Guid id, [FromBody] AddPrestationRequest request, CancellationToken cancellationToken)
    {
        var result = await _addPrestationUseCase.ExecuteAsync(
            id, request.LibellePrestation, request.Tarif, request.DureeMinutes, cancellationToken);
        return result.Status == "not_found"
            ? NotFound()
            : StatusCode(StatusCodes.Status201Created, new { idPrestation = result.IdPrestation });
    }
}
