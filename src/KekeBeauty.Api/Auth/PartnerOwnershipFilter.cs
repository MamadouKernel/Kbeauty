using KekeBeauty.Application.Partner;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;

namespace KekeBeauty.Api.Auth;

/// <summary>
/// Verifie que l'etablissement cible (route {id}) appartient au partenaire identifie par l'en-tete
/// X-Partner-Id (voir specs/006-gestion-prestations/research.md, Decision 2 - dette technique :
/// pas de session/JWT, identite portee directement par le client).
/// </summary>
public sealed class PartnerOwnershipFilter : IAsyncActionFilter
{
    private readonly IPartnerPrestationRepository _repository;

    public PartnerOwnershipFilter(IPartnerPrestationRepository repository)
    {
        _repository = repository;
    }

    public async Task OnActionExecutionAsync(ActionExecutingContext context, ActionExecutionDelegate next)
    {
        if (!context.HttpContext.Request.Headers.TryGetValue("X-Partner-Id", out var partnerIdHeader) ||
            !Guid.TryParse(partnerIdHeader, out var partnerId))
        {
            context.Result = new UnauthorizedObjectResult(new { status = "unauthorized" });
            return;
        }

        if (!context.RouteData.Values.TryGetValue("id", out var idValue) || !Guid.TryParse(idValue?.ToString(), out var idEtablissement))
        {
            context.Result = new BadRequestObjectResult(new { status = "invalid_route" });
            return;
        }

        var ownerId = await _repository.GetOwnerIdAsync(idEtablissement, context.HttpContext.RequestAborted);
        if (ownerId is null || ownerId != partnerId)
        {
            context.Result = new ObjectResult(new { status = "forbidden" }) { StatusCode = StatusCodes.Status403Forbidden };
            return;
        }

        await next();
    }
}
