using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;

namespace KekeBeauty.Api.Auth;

/// <summary>
/// Authentification administrateur reelle par jeton signe (X-Admin-Token, AdminSessionTokenService)
/// avec RBAC (AdminRoleAttribute) et journal d'audit des actions de mutation. Remplace l'ancienne
/// cle API statique unique decrite dans specs/004-onboarding-partenaire/research.md, Decision 4.
/// </summary>
public sealed class AdminRoleAttribute(params string[] roles) : Attribute { public IReadOnlyList<string> Roles { get; } = roles; }

public sealed class AdminApiKeyFilter : IAsyncActionFilter
{
    private readonly AdminSessionTokenService _tokens;
    private readonly KekeBeauty.Application.Admin.IAdminManagementRepository _audit;
    private readonly ILogger<AdminApiKeyFilter> _logger;

    public AdminApiKeyFilter(AdminSessionTokenService tokens, KekeBeauty.Application.Admin.IAdminManagementRepository audit, ILogger<AdminApiKeyFilter> logger)
    {
        _tokens = tokens;
        _audit = audit;
        _logger = logger;
    }

    public async Task OnActionExecutionAsync(ActionExecutingContext context, ActionExecutionDelegate next)
    {
        context.HttpContext.Request.Headers.TryGetValue("X-Admin-Token", out var token);
        if (!_tokens.TryValidate(token, out var identity))
        {
            context.Result = new UnauthorizedObjectResult(new { status = "unauthorized" });
            return;
        }
        var account = await _audit.FindByIdAsync(identity!.Id, context.HttpContext.RequestAborted);
        if (account is null || !account.Actif)
        {
            context.Result = new UnauthorizedObjectResult(new { status = "compte_admin_inactif" });
            return;
        }
        identity = new AdminIdentity(account.IdAdmin, account.Nom, account.Role);
        context.HttpContext.Items["Admin"] = identity;
        var rule = context.ActionDescriptor.EndpointMetadata.OfType<AdminRoleAttribute>().FirstOrDefault();
        if (rule is not null && !rule.Roles.Contains(identity!.Role) && identity.Role != "SUPER_ADMIN")
        {
            context.Result = new ObjectResult(new { status = "permission_refusee" }) { StatusCode = StatusCodes.Status403Forbidden };
            return;
        }
        var executed = await next();
        if (!HttpMethods.IsGet(context.HttpContext.Request.Method) && executed.Exception is null && (executed.Result is not ObjectResult o || (o.StatusCode ?? 200) < 400))
        {
            var id = context.ActionArguments.Values.OfType<Guid>().Cast<Guid?>().FirstOrDefault();
            var controller = context.RouteData.Values["controller"]?.ToString() ?? "Admin";
            var action = context.RouteData.Values["action"]?.ToString() ?? context.HttpContext.Request.Method;
            var route = context.HttpContext.Request.Path.Value;
            try
            {
                await _audit.AuditAsync(identity.Id, identity.Name, $"{controller}.{action}", controller, id, route, context.HttpContext.Connection.RemoteIpAddress?.ToString(), context.HttpContext.RequestAborted);
            }
            catch (Exception exception)
            {
                _logger.LogError(exception, "Echec d'ecriture du journal administrateur pour {Action}", action);
            }
        }
    }
}
