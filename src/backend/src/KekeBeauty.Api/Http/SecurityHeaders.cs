using KekeBeauty.SharedKernel;
using System.Diagnostics;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.HttpOverrides;

namespace KekeBeauty.Api.Http;

/// <summary>Cross-cutting hardening: real client address behind the proxy, response headers, request correlation, global rate limit.</summary>
public static class SecurityHeaders
{
    public const string RequestIdHeader = "X-Request-Id";

    /// <summary>Loopback, RFC 1918 and unique-local/link-local ranges: where a reverse proxy in front of the API lives.</summary>
    public static readonly string[] DefaultProxyNetworks =
        ["127.0.0.0/8", "::1/128", "10.0.0.0/8", "172.16.0.0/12", "192.168.0.0/16", "fc00::/7", "fe80::/10"];

    /// <summary>
    /// Honours <c>X-Forwarded-For/Proto</c> when <c>Proxy:TrustForwardedHeaders</c> is set (the API is then only reachable through the
    /// reverse proxy). Without it every visitor would share the proxy's address, and so one rate-limit bucket.
    /// </summary>
    public static IServiceCollection AddProxyAwareness(this IServiceCollection services, IConfiguration configuration)
    {
        if (!configuration.GetValue("Proxy:TrustForwardedHeaders", false))
        {
            return services;
        }

        return services.Configure<ForwardedHeadersOptions>(options =>
        {
            options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
            options.ForwardLimit = 1;
            // Forwarded headers are believed only from the reverse proxy, which is always on a private network. From anywhere else (the API
            // reached directly by mistake, a public address) they are ignored: otherwise a rotating X-Forwarded-For hands the caller a fresh
            // rate-limit bucket for every request. Proxy:KnownNetworks (CIDR list) narrows or replaces these ranges.
            options.KnownIPNetworks.Clear();
            options.KnownProxies.Clear();
            var configured = configuration.GetSection("Proxy:KnownNetworks").Get<string[]>();
            foreach (var cidr in configured is { Length: > 0 } ? configured : DefaultProxyNetworks)
            {
                options.KnownIPNetworks.Add(System.Net.IPNetwork.Parse(cidr));
            }
        });
    }

    public static IApplicationBuilder UseProxyAwareness(this WebApplication app) =>
        app.Configuration.GetValue("Proxy:TrustForwardedHeaders", false) ? app.UseForwardedHeaders() : app;

    /// <summary>A generous per-address ceiling on the whole API; the sensitive endpoints keep their own, stricter policies.</summary>
    public static IServiceCollection AddGlobalRateLimit(this IServiceCollection services, IConfiguration configuration)
    {
        var limit = configuration.GetValue("RateLimits:GlobalPerMinute", 600);
        return services.AddRateLimiter(options => options.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(context =>
        {
            var path = context.Request.Path;
            if (!path.StartsWithSegments("/api"))
            {
                return RateLimitPartition.GetNoLimiter("static");
            }

            return SharedRateLimit.FixedWindow(context.RequestServices, "global", context.Connection.RemoteIpAddress?.ToString() ?? "unknown", limit, TimeSpan.FromMinutes(1));
        }));
    }

    public static IApplicationBuilder UseSecurityHeaders(this IApplicationBuilder app) =>
        app.Use(async (context, next) =>
        {
            var headers = context.Response.Headers;
            context.Response.OnStarting(() =>
            {
                headers.XContentTypeOptions = "nosniff";
                headers.XFrameOptions = "DENY";
                headers["Referrer-Policy"] = "strict-origin-when-cross-origin";
                headers["Permissions-Policy"] = "camera=(), microphone=(), geolocation=(self)";
                headers["Cross-Origin-Opener-Policy"] = "same-origin";
                headers["Cross-Origin-Resource-Policy"] = "same-origin";
                headers[RequestIdHeader] = Activity.Current?.TraceId.ToString() ?? context.TraceIdentifier;
                if (context.Request.IsHttps)
                {
                    headers.StrictTransportSecurity = "max-age=31536000; includeSubDomains";
                }

                // API answers carry personal or account data: never let a shared cache keep them.
                if (context.Request.Path.StartsWithSegments("/api") && !headers.ContainsKey("Cache-Control"))
                {
                    headers.CacheControl = "no-store";
                }

                return Task.CompletedTask;
            });

            await next();
        });
}
