using KekeBeauty.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace KekeBeauty.Api.Endpoints;

public sealed record CountryDto(Guid Id, string Iso2, string Name, string PhonePrefix, string CurrencyCode);

public sealed record GeoItemDto(Guid Id, string Name);

public static class GeographyEndpoints
{
    public static IEndpointRouteBuilder MapGeographyEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1/geo").WithTags("Geography");

        group.MapGet("/countries", async (KekeBeautyDbContext db, CancellationToken ct) =>
            await db.Countries
                .AsNoTracking()
                .Where(c => c.IsActive)
                .OrderBy(c => c.Name)
                .Select(c => new CountryDto(c.Id, c.Iso2, c.Name, c.PhonePrefix, c.CurrencyCode))
                .ToListAsync(ct));

        group.MapGet("/countries/{countryId:guid}/regions", async (Guid countryId, KekeBeautyDbContext db, CancellationToken ct) =>
            await db.Regions
                .AsNoTracking()
                .Where(r => r.IsActive && EF.Property<Guid>(r, "CountryId") == countryId)
                .OrderBy(r => r.Name)
                .Select(r => new GeoItemDto(r.Id, r.Name))
                .ToListAsync(ct));

        group.MapGet("/regions/{regionId:guid}/cities", async (Guid regionId, KekeBeautyDbContext db, CancellationToken ct) =>
            await db.Cities
                .AsNoTracking()
                .Where(c => c.IsActive && EF.Property<Guid>(c, "RegionId") == regionId)
                .OrderBy(c => c.Name)
                .Select(c => new GeoItemDto(c.Id, c.Name))
                .ToListAsync(ct));

        group.MapGet("/cities/{cityId:guid}/communes", async (Guid cityId, KekeBeautyDbContext db, CancellationToken ct) =>
            await db.Communes
                .AsNoTracking()
                .Where(c => c.IsActive && EF.Property<Guid>(c, "CityId") == cityId)
                .OrderBy(c => c.Name)
                .Select(c => new GeoItemDto(c.Id, c.Name))
                .ToListAsync(ct));

        return app;
    }
}
