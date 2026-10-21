using System.Text;
using System.Threading.RateLimiting;
using KekeBeauty.Modules.Identity.Application;
using KekeBeauty.Modules.Identity.Domain;
using KekeBeauty.Modules.Identity.Infrastructure;
using KekeBeauty.SharedKernel;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;

namespace KekeBeauty.Modules.Identity;

public static class IdentityModule
{
    public const string RateLimitPolicy = "auth";

    /// <summary>Floor for the configured iteration count (OWASP: PBKDF2-HMAC-SHA512 at least 210 000): a lower setting is ignored, never obeyed.</summary>
    public const int MinimumPasswordHashIterations = 210_000;

    public static IServiceCollection AddIdentityModule(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<JwtOptions>()
            .Bind(configuration.GetSection(JwtOptions.SectionName))
            .Validate(o => o.SigningKey.Length >= 32, "Jwt:SigningKey must be provided and be at least 32 characters long.")
            .ValidateOnStart();
        services.AddOptions<AuthOptions>().Bind(configuration.GetSection(AuthOptions.SectionName));

        services.TryAddSingleton(TimeProvider.System);
        // PBKDF2-HMAC-SHA512 at the cost the OWASP password storage guidance asks for (the framework default is 100 000). A hash made with fewer
        // iterations still verifies and is upgraded at the next successful sign-in (PasswordVerificationResult.SuccessRehashNeeded).
        services.AddOptions<PasswordHasherOptions>().Configure<IOptions<AuthOptions>>(
            (options, auth) => options.IterationCount = Math.Max(auth.Value.PasswordHashIterations, MinimumPasswordHashIterations));
        services.TryAddSingleton<IPasswordHasher<User>, PasswordHasher<User>>();
        services.AddSingleton<TokenService>();
        services.AddSingleton<TotpService>();
        services.AddScoped<AuthService>();
        services.AddScoped<AccountService>();
        services.AddScoped<SuperAdminSeeder>();
        services.TryAddSingleton<IEmailSender, LoggingEmailSender>();
        services.TryAddSingleton<IGoogleIdTokenVerifier, GoogleIdTokenVerifier>();
        services.TryAddScoped<ISessionPolicy, DefaultSessionPolicy>();

        services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme).AddJwtBearer();
        services.AddOptions<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme)
            .Configure<IOptions<JwtOptions>, TimeProvider>((bearer, jwtOptions, time) =>
            {
                var jwt = jwtOptions.Value;
                bearer.MapInboundClaims = false;

                // Browsers cannot set headers on a WebSocket: SignalR passes the access token in the query string.
                bearer.Events = new JwtBearerEvents
                {
                    OnMessageReceived = context =>
                    {
                        var token = context.Request.Query["access_token"];
                        if (!string.IsNullOrEmpty(token) && context.HttpContext.Request.Path.StartsWithSegments("/hubs"))
                        {
                            context.Token = token;
                        }

                        return Task.CompletedTask;
                    },
                    OnTokenValidated = async context =>
                    {
                        if (context.Principal?.Identity is not System.Security.Claims.ClaimsIdentity identity)
                        {
                            context.Fail("Invalid principal.");
                            return;
                        }

                        var sub = identity.FindFirst("sub")?.Value;
                        if (!Guid.TryParse(sub, out var userId))
                        {
                            context.Fail("Invalid subject claim.");
                            return;
                        }

                        var db = context.HttpContext.RequestServices.GetRequiredService<IIdentityDbContext>();
                        var now = time.GetUtcNow();
                        var ct = context.HttpContext.RequestAborted;

                        var user = await Microsoft.EntityFrameworkCore.EntityFrameworkQueryableExtensions.FirstOrDefaultAsync(
                            Microsoft.EntityFrameworkCore.EntityFrameworkQueryableExtensions.AsNoTracking(db.Users),
                            u => u.Id == userId && u.DeletedAt == null,
                            ct);
                        if (user is null || user.Status != UserStatus.Active)
                        {
                            context.Fail("Account is not active.");
                            return;
                        }

                        var sid = identity.FindFirst("sid")?.Value;
                        if (Guid.TryParse(sid, out var sessionId))
                        {
                            var sessionActive = await Microsoft.EntityFrameworkCore.EntityFrameworkQueryableExtensions.AnyAsync(
                                Microsoft.EntityFrameworkCore.EntityFrameworkQueryableExtensions.AsNoTracking(db.RefreshTokens),
                                t => t.UserId == userId && t.SessionId == sessionId && t.RevokedAt == null && t.ExpiresAt > now,
                                ct);
                            if (!sessionActive)
                            {
                                context.Fail("Session has been revoked.");
                                return;
                            }
                        }

                        foreach (var claim in identity.FindAll("role").Where(c => AppRoles.AllAdminRoles.Contains(c.Value) && !user.Roles.Contains(c.Value)).ToList())
                        {
                            identity.RemoveClaim(claim);
                        }

                        foreach (var role in user.Roles)
                        {
                            if (!identity.HasClaim("role", role))
                            {
                                identity.AddClaim(new System.Security.Claims.Claim("role", role));
                            }
                        }
                    },
                };
                bearer.TokenValidationParameters = new TokenValidationParameters
                {
                    ValidIssuer = jwt.Issuer,
                    ValidAudience = jwt.Audience,
                    IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwt.SigningKey)),
                    ValidateIssuerSigningKey = true,
                    ClockSkew = TimeSpan.FromSeconds(30),
                    NameClaimType = "sub",
                    RoleClaimType = "role",
                    // Lifetime is checked against the injected clock so tests can drive time deterministically.
                    LifetimeValidator = (notBefore, expires, _, parameters) =>
                    {
                        var now = time.GetUtcNow().UtcDateTime;
                        return expires is not null
                            && expires.Value + parameters.ClockSkew >= now
                            && (notBefore is null || notBefore.Value - parameters.ClockSkew <= now);
                    },
                };
            });
        services.AddAuthorizationBuilder()
            // Deny by default: every endpoint requires an authenticated user unless it opts out with AllowAnonymous.
            // Public reads (catalog, geography, directory), the auth flows, webhooks and health checks declare AllowAnonymous explicitly.
            .SetFallbackPolicy(new AuthorizationPolicyBuilder().RequireAuthenticatedUser().Build())
            .AddPolicy("Partner", policy => policy.RequireRole(AppRoles.PartnerOwner))
            .AddPolicy("Staff", policy => policy.RequireRole(AppRoles.PartnerStaff))
            .AddPolicy("Admin", policy => policy.RequireRole([.. AppRoles.AllAdminRoles]))
            .AddPolicy("AdminKyc", policy => policy.RequireRole(AppRoles.SuperAdmin, AppRoles.Admin, AppRoles.AdminKyc))
            .AddPolicy("AdminSupport", policy => policy.RequireRole(AppRoles.SuperAdmin, AppRoles.Admin, AppRoles.AdminSupport))
            .AddPolicy("AdminModeration", policy => policy.RequireRole(AppRoles.SuperAdmin, AppRoles.Admin, AppRoles.AdminKyc, AppRoles.AdminSupport))
            .AddPolicy("AdminFinance", policy => policy.RequireRole(AppRoles.SuperAdmin, AppRoles.Admin, AppRoles.AdminAccountant))
            .AddPolicy("AdminConfig", policy => policy.RequireRole(AppRoles.SuperAdmin, AppRoles.Admin))
            .AddPolicy("SuperAdmin", policy => policy.RequireRole(AppRoles.SuperAdmin));

        services.AddRateLimiter(limiter =>
        {
            limiter.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
            limiter.AddPolicy(RateLimitPolicy, context => SharedRateLimit.FixedWindow(
                context.RequestServices,
                "auth",
                context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
                context.RequestServices.GetRequiredService<IOptions<AuthOptions>>().Value.AuthPermitPerMinute,
                TimeSpan.FromMinutes(1)));
        });

        return services;
    }
}
