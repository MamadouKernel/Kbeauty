using System.Security.Claims;
using KekeBeauty.Api.Http;
using KekeBeauty.Infrastructure.Partner;
using KekeBeauty.Infrastructure.Reviews;
using KekeBeauty.Infrastructure.Search;
using KekeBeauty.SharedKernel;
using Microsoft.AspNetCore.Mvc;

namespace KekeBeauty.Api.Endpoints;

/// <summary>Clients' reviews with the establishment's answers, and favorites.</summary>
public static class EngagementEndpoints
{
    public static IEndpointRouteBuilder MapEngagementEndpoints(this IEndpointRouteBuilder app)
    {
        // Public: anyone reads the reviews of a published establishment.
        app.MapGet("/api/v1/establishments/{slug}/reviews", async (string slug, int? page, int? pageSize, OpinionService service, CancellationToken ct) =>
                Respond(await service.ListPublicAsync(slug, page ?? 1, pageSize ?? OpinionService.PageSizeDefault, ct)))
            .WithTags("Reviews")
            .Produces<ReviewsPage>().ProducesProblem(StatusCodes.Status404NotFound);

        // Client: a review for a completed appointment, and the list of what was already reviewed.
        app.MapPost("/api/v1/appointments/{id:guid}/review", async (Guid id, ReviewInput input, ClaimsPrincipal user, OpinionService service, CancellationToken ct) =>
            {
                var result = await service.CreateAsync(user.UserId(), id, input, ct);
                return result.IsSuccess ? Results.Created("/api/v1/me/reviews", result.Value) : ProblemResults.From(result.Error);
            })
            .WithTags("Reviews").RequireAuthorization()
            .Produces<ReviewDto>(StatusCodes.Status201Created).ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status404NotFound).ProducesProblem(StatusCodes.Status409Conflict);

        // Client: before / after photos on her own review.
        app.MapPost("/api/v1/reviews/{id:guid}/photos", async (Guid id, IFormFile file, [FromForm] string? kind, ClaimsPrincipal user, ReviewPhotoService service, CancellationToken ct) =>
            {
                if (file.Length > ImageProcessor.MaxUploadBytes)
                {
                    return ProblemResults.From(new Error("file.too_large", "Le fichier dépasse 10 Mo."));
                }

                await using var stream = file.OpenReadStream();
                var result = await service.AddAsync(user.UserId(), id, kind, stream, ct);
                return result.IsSuccess ? Results.Created($"/api/v1/reviews/{id}/photos/{result.Value.Id}", result.Value) : ProblemResults.From(result.Error);
            })
            .WithTags("Reviews").RequireAuthorization()
            .DisableAntiforgery()
            .WithMetadata(new RequestSizeLimitAttribute(PartnerEndpoints.UploadRequestLimit))
            .Accepts<IFormFile>("multipart/form-data")
            .Produces<ReviewPhotoDto>(StatusCodes.Status201Created).ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status404NotFound).ProducesProblem(StatusCodes.Status409Conflict).ProducesProblem(StatusCodes.Status413PayloadTooLarge);

        app.MapDelete("/api/v1/reviews/{id:guid}/photos/{photoId:guid}", async (Guid id, Guid photoId, ClaimsPrincipal user, ReviewPhotoService service, CancellationToken ct) =>
                PartnerEndpoints.NoContentOr(await service.DeleteAsync(user.UserId(), id, photoId, ct)))
            .WithTags("Reviews").RequireAuthorization()
            .Produces(StatusCodes.Status204NoContent).ProducesProblem(StatusCodes.Status404NotFound);

        app.MapGet("/api/v1/me/reviews", async (ClaimsPrincipal user, OpinionService service, CancellationToken ct) =>
                Results.Ok(await service.MineAsync(user.UserId(), ct)))
            .WithTags("Reviews").RequireAuthorization()
            .Produces<IReadOnlyList<MyReviewDto>>();

        // Favorites.
        var favorites = app.MapGroup("/api/v1/me/favorites").WithTags("Favorites").RequireAuthorization();
        favorites.MapGet("/", async (ClaimsPrincipal user, FavoriteService service, CancellationToken ct) =>
                Results.Ok(await service.ListAsync(user.UserId(), ct)))
            .Produces<IReadOnlyList<EstablishmentSummary>>();
        favorites.MapGet("/ids", async (ClaimsPrincipal user, FavoriteService service, CancellationToken ct) =>
                Results.Ok(await service.IdsAsync(user.UserId(), ct)))
            .Produces<IReadOnlyList<Guid>>();
        favorites.MapPut("/{establishmentId:guid}", async (Guid establishmentId, ClaimsPrincipal user, FavoriteService service, CancellationToken ct) =>
                PartnerEndpoints.NoContentOr(await service.AddAsync(user.UserId(), establishmentId, ct)))
            .Produces(StatusCodes.Status204NoContent).ProducesProblem(StatusCodes.Status404NotFound).ProducesProblem(StatusCodes.Status409Conflict);
        favorites.MapDelete("/{establishmentId:guid}", async (Guid establishmentId, ClaimsPrincipal user, FavoriteService service, CancellationToken ct) =>
            {
                await service.RemoveAsync(user.UserId(), establishmentId, ct);
                return Results.NoContent();
            })
            .Produces(StatusCodes.Status204NoContent);

        // Partner: read the reviews of one's establishment and answer them.
        var partner = app.MapGroup("/api/v1/pro").WithTags("Reviews").RequireAuthorization(PartnerEndpoints.PartnerPolicy);
        partner.MapGet("/establishments/{id:guid}/reviews", async (Guid id, int? page, int? pageSize, ClaimsPrincipal user, OpinionService service, CancellationToken ct) =>
                Respond(await service.ListForOwnerAsync(user.UserId(), id, page ?? 1, pageSize ?? OpinionService.PageSizeDefault, ct)))
            .Produces<ReviewsPage>().ProducesProblem(StatusCodes.Status404NotFound);
        partner.MapPost("/reviews/{id:guid}/reply", async (Guid id, ReplyInput input, ClaimsPrincipal user, OpinionService service, CancellationToken ct) =>
                Respond(await service.ReplyAsync(user.UserId(), id, input, ct)))
            .Produces<ReviewDto>().ProducesProblem(StatusCodes.Status400BadRequest).ProducesProblem(StatusCodes.Status404NotFound);

        // Admin: hide an abusive review (with the reason) or restore it.
        var admin = app.MapGroup("/api/v1/admin/reviews").WithTags("Admin").RequireAuthorization(AdminReviewEndpoints.AdminSupportPolicy);
        admin.MapPost("/{id:guid}/hide", async (Guid id, HideReviewInput input, ClaimsPrincipal user, OpinionService service, CancellationToken ct) =>
                PartnerEndpoints.NoContentOr(await service.SetHiddenAsync(user.UserId(), id, hidden: true, input.Reason, ct)))
            .Produces(StatusCodes.Status204NoContent).ProducesProblem(StatusCodes.Status400BadRequest).ProducesProblem(StatusCodes.Status404NotFound);
        admin.MapPost("/{id:guid}/restore", async (Guid id, ClaimsPrincipal user, OpinionService service, CancellationToken ct) =>
                PartnerEndpoints.NoContentOr(await service.SetHiddenAsync(user.UserId(), id, hidden: false, reason: null, ct)))
            .Produces(StatusCodes.Status204NoContent).ProducesProblem(StatusCodes.Status404NotFound);

        return app;
    }

    private static IResult Respond<T>(Result<T> result) => result.IsSuccess ? Results.Ok(result.Value) : ProblemResults.From(result.Error);
}
