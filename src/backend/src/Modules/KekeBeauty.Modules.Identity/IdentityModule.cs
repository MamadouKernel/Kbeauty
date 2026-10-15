using System.Text;
using System.Threading.RateLimiting;
using KekeBeauty.Modules.Identity.Application;
using KekeBeauty.Modules.Identity.Domain;
using KekeBeauty.Modules.Identity.Infrastructure;
using Microsoft.AspNetCore.Authentication.JwtBearer;
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

    public static IServiceCollection AddIdentityModule(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<JwtOptions>()
            .Bind(configuration.GetSection(JwtOptions.SectionName))
            .Validate(o => o.SigningKey.Length >= 32, "Jwt:SigningKey must be provided and be at least 32 characters long.")
            .ValidateOnStart();
        services.AddOptions<AuthOptions>().Bind(configuration.GetSection(AuthOptions.SectionName));

        services.TryAddSingleton(TimeProvider.System);
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
            .AddPolicy("Partner", policy => policy.RequireRole(AppRoles.PartnerOwner))
            .AddPolicy("Admin", policy => policy.RequireRole(AppRoles.Admin, AppRoles.SuperAdmin))
            .AddPolicy("SuperAdmin", policy => policy.RequireRole(AppRoles.SuperAdmin));

        services.AddRateLimiter(limiter =>
        {
            limiter.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
            limiter.AddPolicy(RateLimitPolicy, context => RateLimitPartition.GetFixedWindowLimiter(
                context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
                _ => new FixedWindowRateLimiterOptions
                {
                    PermitLimit = context.RequestServices.GetRequiredService<IOptions<AuthOptions>>().Value.AuthPermitPerMinute,
                    Window = TimeSpan.FromMinutes(1),
                }));
        });

        return services;
    }
}
