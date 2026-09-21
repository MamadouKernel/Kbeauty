using KekeBeauty.Application.Staff;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;

namespace KekeBeauty.Api.Auth;

/// <summary>
/// Meme principe que PartnerOwnershipFilter (pas de session/JWT, identite portee par le client via
/// en-tete) : X-Collaborateur-Id porte l'id_utilisateur du compte COLLABORATEUR connecte. Resout
/// l'id_collaborateur correspondant et le place dans HttpContext.Items pour le controleur.
/// </summary>
public sealed class StaffAuthFilter : IAsyncActionFilter
{
    public const string ContextKey = "IdCollaborateur";

    private readonly IStaffRepository _repository;
    private readonly UserSessionTokenService _tokens;

    public StaffAuthFilter(IStaffRepository repository, UserSessionTokenService tokens)
    {
        _repository = repository;
        _tokens = tokens;
    }

    public async Task OnActionExecutionAsync(ActionExecutingContext context, ActionExecutionDelegate next)
    {
        var tokenValues = context.HttpContext.Request.Headers["X-Staff-Token"];
        if (tokenValues.Count != 1 || !_tokens.TryValidate(tokenValues[0], "STAFF", out var idUtilisateur))
        {
            context.Result = new UnauthorizedObjectResult(new { status = "unauthorized" });
            return;
        }

        var idCollaborateur = await _repository.GetIdCollaborateurByUtilisateurAsync(idUtilisateur, context.HttpContext.RequestAborted);
        if (idCollaborateur is null)
        {
            context.Result = new ObjectResult(new { status = "compte_non_reference" }) { StatusCode = StatusCodes.Status403Forbidden };
            return;
        }

        context.HttpContext.Items[ContextKey] = idCollaborateur.Value;
        await next();
    }
}
