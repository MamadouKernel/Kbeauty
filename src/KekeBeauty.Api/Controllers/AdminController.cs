using KekeBeauty.Api.Auth;
using KekeBeauty.Application.Onboarding;
using Microsoft.AspNetCore.Mvc;

namespace KekeBeauty.Api.Controllers;

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

    public AdminController(
        ListPendingApplicationsUseCase listUseCase,
        ValidateApplicationUseCase validateUseCase,
        RejectApplicationUseCase rejectUseCase,
        IEtablissementRepository etablissementRepository,
        IFileStorage fileStorage)
    {
        _listUseCase = listUseCase;
        _validateUseCase = validateUseCase;
        _rejectUseCase = rejectUseCase;
        _etablissementRepository = etablissementRepository;
        _fileStorage = fileStorage;
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
}
