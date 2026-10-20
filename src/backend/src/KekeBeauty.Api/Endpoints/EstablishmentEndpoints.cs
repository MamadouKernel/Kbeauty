using System.Threading.RateLimiting;
using KekeBeauty.Infrastructure.Search;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Mvc;

namespace KekeBeauty.Api.Endpoints;

/// <summary>Query-string contract of the directory search (all optional).</summary>
public sealed class EstablishmentSearchRequest
{
    [FromQuery(Name = "category")]
    public string? Category { get; init; }

    [FromQuery(Name = "cityId")]
    public Guid? CityId { get; init; }

    [FromQuery(Name = "communeId")]
    public Guid? CommuneId { get; init; }

    [FromQuery(Name = "lat")]
    public double? Lat { get; init; }

    [FromQuery(Name = "lng")]
    public double? Lng { get; init; }

    [FromQuery(Name = "radiusKm")]
    public double? RadiusKm { get; init; }

    [FromQuery(Name = "q")]
    public string? Q { get; init; }

    [FromQuery(Name = "bookable")]
    public bool? Bookable { get; init; }

    [FromQuery(Name = "sort")]
    public string? Sort { get; init; }

    [FromQuery(Name = "page")]
    public int? Page { get; init; }

    [FromQuery(Name = "pageSize")]
    public int? PageSize { get; init; }
}

public sealed record EstablishmentEventRequest(string Type);

public static class EstablishmentEndpoints
{
    public const string EventsRateLimitPolicy = "events";

    private static readonly string[] SortModes = ["featured", "distance", "name", "recent", "rating"];

    public static IServiceCollection AddEstablishmentRateLimits(this IServiceCollection services) =>
        services.AddRateLimiter(limiter =>
        {
            limiter.AddPolicy(EventsRateLimitPolicy, context =>
                RateLimitPartition.GetFixedWindowLimiter(
                    context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
                    _ => new FixedWindowRateLimiterOptions { PermitLimit = 120, Window = TimeSpan.FromMinutes(1) }));

            // Booking and calendar reads: generous for a person clicking through days, tight for scripts.
            limiter.AddPolicy(AppointmentEndpoints.BookingRateLimitPolicy, context =>
                RateLimitPartition.GetFixedWindowLimiter(
                    context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
                    _ => new FixedWindowRateLimiterOptions
                    {
                        PermitLimit = context.RequestServices.GetRequiredService<IConfiguration>().GetValue("RateLimits:BookingPerMinute", 90),
                        Window = TimeSpan.FromMinutes(1),
                    }));
        });

    public static IEndpointRouteBuilder MapEstablishmentEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1/establishments").WithTags("Establishments").AllowAnonymous();

        group.MapGet("/", async ([AsParameters] EstablishmentSearchRequest request, EstablishmentQueries queries, CancellationToken ct) =>
        {
            var errors = Validate(request);
            if (errors.Count > 0)
            {
                return Results.ValidationProblem(errors);
            }

            var criteria = new EstablishmentSearchCriteria(
                request.Category, request.CityId, request.CommuneId, request.Lat, request.Lng, request.RadiusKm,
                request.Q, request.Bookable, request.Sort, request.Page ?? 1, request.PageSize ?? 20);
            return Results.Ok(await queries.SearchAsync(criteria, ct));
        })
        .Produces<EstablishmentPage>()
        .ProducesValidationProblem();

        group.MapGet("/{slug}", async (string slug, EstablishmentQueries queries, CancellationToken ct) =>
            await queries.GetBySlugAsync(slug, ct) is { } details ? Results.Ok(details) : Results.NotFound())
        .Produces<EstablishmentDetails>()
        .ProducesProblem(StatusCodes.Status404NotFound);

        group.MapPost("/{id:guid}/events", async (Guid id, EstablishmentEventRequest request, EstablishmentQueries queries, CancellationToken ct) =>
        {
            if (!Enum.TryParse<EstablishmentEventKind>(request.Type, ignoreCase: true, out var kind) || !Enum.IsDefined(kind))
            {
                return Results.ValidationProblem(new Dictionary<string, string[]> { ["type"] = ["Type attendu : view, call ou directions."] });
            }

            return await queries.RecordEventAsync(id, kind, ct) ? Results.NoContent() : Results.NotFound();
        })
        .RequireRateLimiting(EventsRateLimitPolicy)
        .Produces(StatusCodes.Status204NoContent)
        .ProducesProblem(StatusCodes.Status404NotFound)
        .ProducesValidationProblem();

        return app;
    }

    private static Dictionary<string, string[]> Validate(EstablishmentSearchRequest r)
    {
        var errors = new Dictionary<string, string[]>();

        if (r.Lat is < -90 or > 90)
        {
            errors["lat"] = ["La latitude doit être comprise entre -90 et 90."];
        }

        if (r.Lng is < -180 or > 180)
        {
            errors["lng"] = ["La longitude doit être comprise entre -180 et 180."];
        }

        if ((r.Lat is null) != (r.Lng is null))
        {
            errors["lat"] = ["Latitude et longitude doivent être fournies ensemble."];
        }

        if (r.RadiusKm is { } radius && (radius is <= 0 or > 100 || r.Lat is null))
        {
            errors["radiusKm"] = ["Le rayon (0 à 100 km) nécessite une position (lat, lng)."];
        }

        if (r.Page is < 1)
        {
            errors["page"] = ["La page commence à 1."];
        }

        if (r.PageSize is < 1 or > EstablishmentQueries.MaxPageSize)
        {
            errors["pageSize"] = [$"La taille de page doit être comprise entre 1 et {EstablishmentQueries.MaxPageSize}."];
        }

        if (r.Sort is { } sort && !SortModes.Contains(sort))
        {
            errors["sort"] = [$"Tri invalide. Valeurs possibles : {string.Join(", ", SortModes)}."];
        }

        if (r.Sort == "distance" && r.Lat is null)
        {
            errors["sort"] = ["Le tri par distance nécessite une position (lat, lng)."];
        }

        if (r.Q is { Length: > 100 })
        {
            errors["q"] = ["La recherche est limitée à 100 caractères."];
        }

        return errors;
    }
}
