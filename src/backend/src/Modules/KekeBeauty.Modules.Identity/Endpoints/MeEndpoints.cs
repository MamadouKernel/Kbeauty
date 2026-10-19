using System.Security.Claims;
using KekeBeauty.Modules.Identity.Application;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace KekeBeauty.Modules.Identity.Endpoints;

public static class MeEndpoints
{
    public static IEndpointRouteBuilder MapMeEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1/me").WithTags("Account").RequireAuthorization();

        group.MapGet("/", async (ClaimsPrincipal principal, AccountService accounts, CancellationToken ct) =>
            await accounts.GetMeAsync(UserId(principal), ct) is { } me ? Results.Ok(me) : Results.NotFound());

        group.MapPatch("/", async (UpdateProfileRequest request, ClaimsPrincipal principal, AccountService accounts, CancellationToken ct) =>
        {
            var result = await accounts.UpdateProfileAsync(UserId(principal), request, ct);
            return result.IsSuccess
                ? Results.Ok(result.Value)
                : Results.Problem(title: result.Error.Message, statusCode: StatusCodes.Status400BadRequest, extensions: new Dictionary<string, object?> { ["code"] = result.Error.Code });
        });

        group.MapGet("/sessions", async (ClaimsPrincipal principal, AccountService accounts, CancellationToken ct) =>
            Results.Ok(await accounts.ListSessionsAsync(UserId(principal), SessionId(principal), ct)));

        group.MapDelete("/sessions/{sessionId:guid}", async (Guid sessionId, ClaimsPrincipal principal, AccountService accounts, CancellationToken ct) =>
            await accounts.RevokeSessionAsync(UserId(principal), sessionId, ct) ? Results.NoContent() : Results.NotFound());

        group.MapGet("/trusted-devices", async (ClaimsPrincipal principal, HttpContext http, AccountService accounts, CancellationToken ct) =>
            Results.Ok(await accounts.ListTrustedDevicesAsync(UserId(principal), http.Request.Cookies[AuthOptions.TrustedDeviceCookieName], ct)));

        group.MapDelete("/trusted-devices/{deviceId:guid}", async (Guid deviceId, ClaimsPrincipal principal, AccountService accounts, CancellationToken ct) =>
            await accounts.RevokeTrustedDeviceAsync(UserId(principal), deviceId, ct) ? Results.NoContent() : Results.NotFound());

        group.MapGet("/whatsapp", async (ClaimsPrincipal principal, AuthService auth, CancellationToken ct) =>
            Results.Ok(await auth.WhatsAppStatusAsync(UserId(principal), ct)));

        group.MapPost("/whatsapp/link", async (WhatsAppRequest request, ClaimsPrincipal principal, AuthService auth, CancellationToken ct) =>
            Answer(await auth.BeginWhatsAppLinkAsync(UserId(principal), request.Phone, ct), () => Results.Accepted()));

        group.MapPost("/whatsapp/verify", async (PhoneCodeRequest request, ClaimsPrincipal principal, AuthService auth, CancellationToken ct) =>
            Answer(await auth.ConfirmWhatsAppLinkAsync(UserId(principal), request.Code, ct), Results.NoContent));

        group.MapDelete("/whatsapp", async (ClaimsPrincipal principal, AuthService auth, CancellationToken ct) =>
            Answer(await auth.UnlinkWhatsAppAsync(UserId(principal), ct), Results.NoContent));

        return app;
    }

    private static IResult Answer(KekeBeauty.SharedKernel.Result result, Func<IResult> onSuccess) =>
        result.IsSuccess
            ? onSuccess()
            : Results.Problem(
                title: result.Error.Message,
                statusCode: result.Error.Code == "auth.provider_disabled" ? StatusCodes.Status404NotFound : StatusCodes.Status400BadRequest,
                extensions: new Dictionary<string, object?> { ["code"] = result.Error.Code });

    private static Guid UserId(ClaimsPrincipal principal) => Guid.Parse(principal.FindFirstValue("sub")!);

    private static Guid? SessionId(ClaimsPrincipal principal) =>
        Guid.TryParse(principal.FindFirstValue("sid"), out var sid) ? sid : null;
}
