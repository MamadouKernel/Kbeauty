using System.Security.Claims;
using KekeBeauty.Api.Http;
using KekeBeauty.Infrastructure.Partner;
using KekeBeauty.Modules.Onboarding.Domain;

namespace KekeBeauty.Api.Endpoints;

/// <summary>Admin review queue (<c>/api/v1/admin/kyc</c>). Admin or SuperAdmin role required.</summary>
public static class AdminReviewEndpoints
{
    public const string AdminPolicy = "Admin";
    public const string AdminKycPolicy = "AdminKyc";
    public const string AdminSupportPolicy = "AdminSupport";
    public const string AdminModerationPolicy = "AdminModeration";
    public const string AdminFinancePolicy = "AdminFinance";
    public const string AdminConfigPolicy = "AdminConfig";

    public static IEndpointRouteBuilder MapAdminReviewEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1/admin/kyc").WithTags("Admin").RequireAuthorization(AdminKycPolicy);

        group.MapGet("/queue", async (string? status, int? page, int? pageSize, ReviewService service, CancellationToken ct) =>
            {
                var parsed = KycStatus.PendingReview;
                if (status is not null && (!Enum.TryParse(status, ignoreCase: true, out parsed) || !Enum.IsDefined(parsed)))
                {
                    return Results.ValidationProblem(new Dictionary<string, string[]> { ["status"] = ["Statut inconnu."] });
                }

                return Results.Ok(await service.QueueAsync(parsed, page ?? 1, pageSize ?? 20, ct));
            })
            .Produces<QueuePage>()
            .ProducesValidationProblem();

        group.MapGet("/{reviewId:guid}", async (Guid reviewId, ReviewService service, CancellationToken ct) =>
                await service.GetAsync(reviewId, ct) is { } detail ? Results.Ok(detail) : ProblemResults.From(ReviewService.ReviewNotFound))
            .Produces<ReviewDetail>()
            .ProducesProblem(StatusCodes.Status404NotFound);

        group.MapGet("/{reviewId:guid}/documents/{side}", async (Guid reviewId, string side, ClaimsPrincipal user, HttpContext http, ReviewService service, CancellationToken ct) =>
            {
                if (!Enum.TryParse<DocumentSide>(side, ignoreCase: true, out var parsed) || !Enum.IsDefined(parsed))
                {
                    return ProblemResults.From(ReviewService.DocumentNotFound);
                }

                var result = await service.OpenDocumentAsync(user.UserId(), reviewId, parsed, http.Connection.RemoteIpAddress?.ToString(), ct);
                if (result.IsFailure)
                {
                    return ProblemResults.From(result.Error);
                }

                http.Response.Headers.CacheControl = "no-store, private";
                http.Response.Headers.XContentTypeOptions = "nosniff";
                return Results.Stream(result.Value.Content, ImageProcessor.WebpContentType);
            })
            .Produces(StatusCodes.Status200OK, contentType: ImageProcessor.WebpContentType)
            .ProducesProblem(StatusCodes.Status404NotFound);

        group.MapPost("/{reviewId:guid}/decision", async (Guid reviewId, DecisionInput input, ClaimsPrincipal user, HttpContext http, ReviewService service, CancellationToken ct) =>
            {
                var result = await service.DecideAsync(user.UserId(), reviewId, input, http.Connection.RemoteIpAddress?.ToString(), ct);
                return result.IsSuccess ? Results.Ok(result.Value) : ProblemResults.From(result.Error);
            })
            .Produces<ReviewInfo>()
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict);

        return app;
    }
}
