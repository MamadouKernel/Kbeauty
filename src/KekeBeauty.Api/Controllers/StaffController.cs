using KekeBeauty.Api.Auth;
using KekeBeauty.Application.Staff;
using Microsoft.AspNetCore.Mvc;

namespace KekeBeauty.Api.Controllers;

public sealed record FicheTechniqueBody(string Notes);
public sealed record WorkSessionBody(string Action);

[ApiController]
[Route("staff")]
[ServiceFilter(typeof(StaffAuthFilter))]
public sealed class StaffController : ControllerBase
{
    private readonly StaffPortalUseCase _useCase;

    public StaffController(StaffPortalUseCase useCase)
    {
        _useCase = useCase;
    }

    private Guid IdCollaborateur => (Guid)HttpContext.Items[StaffAuthFilter.ContextKey]!;

    [HttpGet("planning")]
    public async Task<IActionResult> GetPlanning(CancellationToken cancellationToken) =>
        Ok(await _useCase.GetPlanningAsync(IdCollaborateur, cancellationToken));

    [HttpGet("commissions")]
    public async Task<IActionResult> GetCommissions(CancellationToken cancellationToken) =>
        Ok(await _useCase.GetCommissionsAsync(IdCollaborateur, cancellationToken));

    [HttpGet("rdv/{id:guid}/fiche-technique")]
    public async Task<IActionResult> GetFicheTechnique(Guid id, CancellationToken cancellationToken)
    {
        var notes = await _useCase.GetFicheTechniqueAsync(id, IdCollaborateur, cancellationToken);
        return Ok(new { notes });
    }

    [HttpPut("rdv/{id:guid}/fiche-technique")]
    public async Task<IActionResult> SaveFicheTechnique(Guid id, [FromBody] FicheTechniqueBody body, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(body.Notes))
        {
            return BadRequest(new { status = "notes_requises" });
        }

        var saved = await _useCase.EnregistrerFicheTechniqueAsync(id, IdCollaborateur, body.Notes, cancellationToken);
        return saved ? Ok(new { status = "saved" }) : NotFound();
    }

    [HttpPost("rdv/{id:guid}/session")]
    public async Task<IActionResult> UpdateWorkSession(Guid id,[FromBody]WorkSessionBody body,CancellationToken cancellationToken)
    {
        var action=body.Action?.Trim().ToLowerInvariant()??string.Empty;
        if(action is not("start" or "pause" or "resume" or "finish"))return BadRequest(new{status="action_invalide"});
        var result=await _useCase.UpdateWorkSessionAsync(id,IdCollaborateur,action,cancellationToken);
        return result switch{"started" or "paused" or "resumed" or "finished"=>Ok(new{status=result}),"transition_invalide"=>Conflict(new{status=result}),_=>BadRequest(new{status=result})};
    }}
