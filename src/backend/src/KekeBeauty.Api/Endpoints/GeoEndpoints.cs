using System.Security.Claims;
using System.Threading.RateLimiting;
using KekeBeauty.Api.Http;
using KekeBeauty.Infrastructure.Geo;
using KekeBeauty.SharedKernel;

namespace KekeBeauty.Api.Endpoints;

/// <summary>Address search for the partner address form (V1 parity): signed-in users only, bounded per user, answered from our own cache first.</summary>
public static class GeoEndpoints
{
    public const string RateLimitPolicy = "geo";

    public static IServiceCollection AddGeoRateLimits(this IServiceCollection services) =>
        services.AddRateLimiter(limiter => limiter.AddPolicy(RateLimitPolicy, context =>
            SharedRateLimit.FixedWindow(
                context.RequestServices,
                "geo",
                context.User.FindFirstValue("sub") ?? context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
                context.RequestServices.GetRequiredService<IConfiguration>().GetValue("RateLimits:GeoPerMinute", 30),
                TimeSpan.FromMinutes(1))));

    public static IEndpointRouteBuilder MapGeoEndpoints(this IEndpointRouteBuilder app)
    {
        var geo = app.MapGroup("/api/v1/geo").WithTags("Geo").RequireAuthorization().RequireRateLimiting(RateLimitPolicy);

        geo.MapGet("/search", async (string? q, GeocodingService service, CancellationToken ct) =>
                Respond(await service.SearchAsync(q, ct)))
            .Produces<IReadOnlyList<GeoPlace>>()
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status502BadGateway);

        geo.MapGet("/reverse", async (double lat, double lon, GeocodingService service, CancellationToken ct) =>
                Respond(await service.ReverseAsync(lat, lon, ct)))
            .Produces<IReadOnlyList<GeoPlace>>()
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status502BadGateway);

        return app;
    }

    private static IResult Respond(Result<IReadOnlyList<GeoPlace>> result) =>
        result.IsSuccess ? Results.Ok(result.Value) : ProblemResults.From(result.Error);
}
