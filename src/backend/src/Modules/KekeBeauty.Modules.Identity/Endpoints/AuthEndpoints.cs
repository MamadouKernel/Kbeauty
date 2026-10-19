using KekeBeauty.Modules.Identity.Application;
using KekeBeauty.SharedKernel;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Options;

namespace KekeBeauty.Modules.Identity.Endpoints;

public static class AuthEndpoints
{
    private const string CookiePath = "/api/v1/auth";

    public static IEndpointRouteBuilder MapAuthEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1/auth").WithTags("Auth").RequireRateLimiting(IdentityModule.RateLimitPolicy);

        group.MapGet("/providers", (IOptions<AuthOptions> options) => new ProvidersResponse(
            Password: true,
            Google: new ProviderStatus(options.Value.Google.Enabled, options.Value.Google.Enabled ? options.Value.Google.ClientId : null),
            WhatsApp: new ProviderStatus(options.Value.WhatsApp.Enabled, null)));

        group.MapPost("/whatsapp/request", async (WhatsAppRequest request, AuthService auth, CancellationToken ct) =>
            Respond(await auth.RequestWhatsAppLoginAsync(request.Phone, ct), () => Results.Accepted()));

        group.MapPost("/whatsapp/verify", async (WhatsAppVerifyRequest request, HttpContext http, AuthService auth, CancellationToken ct) =>
            Respond(await auth.VerifyWhatsAppLoginAsync(request, ct, http.Request.Cookies[AuthOptions.TrustedDeviceCookieName])));

        group.MapPost("/register", async (RegisterRequest request, AuthService auth, CancellationToken ct) =>
            Respond(await auth.RegisterAsync(request, ct), () => Results.Accepted()));

        group.MapPost("/confirm-email", async (ConfirmEmailRequest request, AuthService auth, CancellationToken ct) =>
            Respond(await auth.ConfirmEmailAsync(request.Token, ct), Results.NoContent));

        group.MapPost("/forgot-password", async (ForgotPasswordRequest request, AuthService auth, CancellationToken ct) =>
            Respond(await auth.ForgotPasswordAsync(request.Email, ct), () => Results.Accepted()));

        group.MapPost("/reset-password", async (ResetPasswordRequest request, AuthService auth, CancellationToken ct) =>
            Respond(await auth.ResetPasswordAsync(request, ct), Results.NoContent));

        group.MapPost("/login", async (LoginRequest request, HttpContext http, AuthService auth, CancellationToken ct) =>
            Respond(await auth.LoginAsync(request, ct, http.Request.Cookies[AuthOptions.TrustedDeviceCookieName])));

        group.MapPost("/google", async (GoogleLoginRequest request, HttpContext http, AuthService auth, CancellationToken ct) =>
            Respond(await auth.LoginWithGoogleAsync(request.IdToken, ct, http.Request.Cookies[AuthOptions.TrustedDeviceCookieName])));

        group.MapPost("/2fa/setup", async (ChallengeRequest request, AuthService auth, CancellationToken ct) =>
            Respond(await auth.BeginTwoFactorSetupAsync(request.ChallengeToken, ct)));

        group.MapPost("/2fa/enable", async (ChallengeCodeRequest request, HttpContext http, AuthService auth, CancellationToken ct) =>
            RespondWithSession(http, await auth.EnableTwoFactorAsync(request, ContextOf(http), ct)));

        group.MapPost("/2fa/verify", async (ChallengeCodeRequest request, HttpContext http, AuthService auth, CancellationToken ct) =>
            RespondWithSession(http, await auth.VerifyTwoFactorAsync(request, ContextOf(http), ct)));

        group.MapPost("/2fa/trusted", async (ChallengeRequest request, HttpContext http, AuthService auth, CancellationToken ct) =>
            RespondWithSession(http, await auth.CompleteTrustedDeviceAsync(request.ChallengeToken, http.Request.Cookies[AuthOptions.TrustedDeviceCookieName], ContextOf(http), ct)));

        group.MapPost("/refresh", async (HttpContext http, AuthService auth, CancellationToken ct) =>
        {
            var result = await auth.RefreshAsync(http.Request.Cookies[AuthOptions.RefreshCookieName], ContextOf(http), ct);
            if (result.IsFailure)
            {
                ClearRefreshCookie(http);
                return Problem(result.Error, StatusCodes.Status401Unauthorized);
            }

            return RespondWithSession(http, result);
        });

        group.MapPost("/logout", async (HttpContext http, AuthService auth, CancellationToken ct) =>
        {
            await auth.LogoutAsync(http.Request.Cookies[AuthOptions.RefreshCookieName], ct);
            ClearRefreshCookie(http);
            return Results.NoContent();
        });

        return app;
    }

    public static ClientContext ContextOf(HttpContext http) =>
        new(http.Connection.RemoteIpAddress?.ToString(), http.Request.Headers.UserAgent.ToString());

    private static IResult Respond(Result result, Func<IResult> onSuccess) =>
        result.IsSuccess ? onSuccess() : Problem(result.Error);

    private static IResult Respond<T>(Result<T> result) =>
        result.IsSuccess ? Results.Ok(result.Value) : Problem(result.Error);

    private static IResult RespondWithSession(HttpContext http, Result<IssuedSession> result)
    {
        if (result.IsFailure)
        {
            return Problem(result.Error);
        }

        var session = result.Value;
        http.Response.Cookies.Append(AuthOptions.RefreshCookieName, session.RefreshToken, new CookieOptions
        {
            HttpOnly = true,
            Secure = http.Request.IsHttps,
            SameSite = SameSiteMode.Strict,
            Path = CookiePath,
            Expires = session.RefreshExpiresAt,
        });
        if (!string.IsNullOrEmpty(session.TrustedDeviceToken) && session.TrustedDeviceExpiresAt is { } tdExpires)
        {
            http.Response.Cookies.Append(AuthOptions.TrustedDeviceCookieName, session.TrustedDeviceToken, new CookieOptions
            {
                HttpOnly = true,
                Secure = http.Request.IsHttps,
                SameSite = SameSiteMode.Strict,
                Path = "/api/v1",
                Expires = tdExpires,
            });
        }

        return Results.Ok(session.Response);
    }

    private static void ClearRefreshCookie(HttpContext http) =>
        http.Response.Cookies.Delete(AuthOptions.RefreshCookieName, new CookieOptions { Path = CookiePath });

    private static IResult Problem(Error error, int? statusOverride = null)
    {
        if (error.Code == AuthErrors.WeakPassword("").Code)
        {
            return Results.ValidationProblem(
                new Dictionary<string, string[]> { ["password"] = [error.Message] },
                title: "Mot de passe trop faible");
        }

        var status = statusOverride ?? error.Code switch
        {
            "auth.invalid_credentials" or "auth.challenge_expired" => StatusCodes.Status401Unauthorized,
            "auth.email_not_confirmed" or "auth.account_suspended" => StatusCodes.Status403Forbidden,
            "auth.account_locked" => StatusCodes.Status423Locked,
            "auth.provider_disabled" => StatusCodes.Status404NotFound,
            _ => StatusCodes.Status400BadRequest,
        };

        return Results.Problem(
            title: error.Message,
            statusCode: status,
            extensions: new Dictionary<string, object?> { ["code"] = error.Code });
    }
}
