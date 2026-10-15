using KekeBeauty.Infrastructure.Admin;

namespace KekeBeauty.Api.Endpoints;

/// <summary>Public reading of the published legal texts (terms, privacy policy, legal notice), maintained by the admins.</summary>
public static class LegalEndpoints
{
    public static IEndpointRouteBuilder MapLegalEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/v1/legal/{slug}", async (string slug, LegalDocumentService service, HttpContext http, CancellationToken ct) =>
            {
                if (!LegalDefaults.Slugs.Contains(slug))
                {
                    return Results.NotFound();
                }

                var document = await service.GetPublishedAsync(slug, ct);
                http.Response.Headers.CacheControl = "no-cache";
                return document is null ? Results.NotFound() : Results.Ok(document);
            })
            .WithTags("Legal").Produces<LegalDocumentDto>().Produces(StatusCodes.Status404NotFound);

        return app;
    }
}
