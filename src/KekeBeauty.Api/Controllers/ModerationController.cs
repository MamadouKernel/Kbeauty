using KekeBeauty.Api.Auth;
using KekeBeauty.Application.Moderation;
using Microsoft.AspNetCore.Mvc;

namespace KekeBeauty.Api.Controllers;

[ApiController]
[ServiceFilter(typeof(AdminApiKeyFilter))]
[AdminRole("SUPPORT")]
public sealed class ModerationController : ControllerBase
{
    private readonly SuspendUtilisateurUseCase _suspendUtilisateurUseCase;
    private readonly SuspendEtablissementUseCase _suspendEtablissementUseCase;

    public ModerationController(SuspendUtilisateurUseCase suspendUtilisateurUseCase, SuspendEtablissementUseCase suspendEtablissementUseCase)
    {
        _suspendUtilisateurUseCase = suspendUtilisateurUseCase;
        _suspendEtablissementUseCase = suspendEtablissementUseCase;
    }

    [HttpPost("admin/utilisateurs/{id:guid}/suspendre")]
    public async Task<IActionResult> SuspendreUtilisateur(Guid id, CancellationToken cancellationToken)
    {
        var found = await _suspendUtilisateurUseCase.ExecuteAsync(id, true, cancellationToken);
        return found ? Ok(new { idUtilisateur = id, estSuspendu = true }) : NotFound();
    }

    [HttpPost("admin/utilisateurs/{id:guid}/reactiver")]
    public async Task<IActionResult> ReactiverUtilisateur(Guid id, CancellationToken cancellationToken)
    {
        var found = await _suspendUtilisateurUseCase.ExecuteAsync(id, false, cancellationToken);
        return found ? Ok(new { idUtilisateur = id, estSuspendu = false }) : NotFound();
    }

    [HttpPost("admin/etablissements/{id:guid}/suspendre")]
    public async Task<IActionResult> SuspendreEtablissement(Guid id, CancellationToken cancellationToken)
    {
        var found = await _suspendEtablissementUseCase.ExecuteAsync(id, true, cancellationToken);
        return found ? Ok(new { idEtablissement = id, estSuspendu = true }) : NotFound();
    }

    [HttpPost("admin/etablissements/{id:guid}/reactiver")]
    public async Task<IActionResult> ReactiverEtablissement(Guid id, CancellationToken cancellationToken)
    {
        var found = await _suspendEtablissementUseCase.ExecuteAsync(id, false, cancellationToken);
        return found ? Ok(new { idEtablissement = id, estSuspendu = false }) : NotFound();
    }
}
