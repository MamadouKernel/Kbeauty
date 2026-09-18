using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;

namespace KekeBeauty.Api.Auth;

/// <summary>
/// Protection minimale temporaire des endpoints admin (voir specs/004-onboarding-partenaire/research.md,
/// Decision 4) : aucune feature d'authentification administrateur n'existe encore. A remplacer par une
/// vraie authentification/RBAC dans une feature dediee.
/// </summary>
public sealed class AdminApiKeyFilter : IActionFilter
{
    private readonly IConfiguration _configuration;

    public AdminApiKeyFilter(IConfiguration configuration)
    {
        _configuration = configuration;
    }

    public void OnActionExecuting(ActionExecutingContext context)
    {
        var expectedKey = _configuration["Admin:ApiKey"];

        if (string.IsNullOrWhiteSpace(expectedKey) ||
            !context.HttpContext.Request.Headers.TryGetValue("X-Admin-Api-Key", out var providedKey) ||
            providedKey != expectedKey)
        {
            context.Result = new UnauthorizedObjectResult(new { status = "unauthorized" });
        }
    }

    public void OnActionExecuted(ActionExecutedContext context)
    {
    }
}
