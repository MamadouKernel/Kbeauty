using KekeBeauty.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace KekeBeauty.Api.Endpoints;

public sealed record CategoryDto(Guid Id, string Name, string Slug, string Icon);

public static class CatalogEndpoints
{
    public static IEndpointRouteBuilder MapCatalogEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1").WithTags("Catalog");

        group.MapGet("/categories", async (KekeBeautyDbContext db, CancellationToken ct) =>
            await db.BeautyCategories
                .AsNoTracking()
                .Where(c => c.IsActive)
                .OrderBy(c => c.SortOrder)
                .Select(c => new CategoryDto(c.Id, c.Name, c.Slug, c.Icon))
                .ToListAsync(ct));

        return app;
    }
}
