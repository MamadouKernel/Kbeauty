using System.Security.Claims;
using Microsoft.AspNetCore.Mvc;
using KekeBeauty.Api.Http;
using KekeBeauty.Infrastructure.Partner;
using KekeBeauty.Modules.Onboarding.Domain;
using KekeBeauty.SharedKernel;

namespace KekeBeauty.Api.Endpoints;

/// <summary>The partner's own identity file (<c>/api/v1/pro/kyc</c>). Documents are write-only here: never served back.</summary>
public static class PartnerKycEndpoints
{
    public static IEndpointRouteBuilder MapPartnerKycEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1/pro/kyc").WithTags("Partner").RequireAuthorization(PartnerEndpoints.PartnerPolicy);

        group.MapGet("/", async (ClaimsPrincipal user, KycService service, CancellationToken ct) =>
                await service.GetAsync(user.UserId(), ct) is { } view
                    ? Results.Ok(view)
                    : ProblemResults.From(new Error("kyc.not_found", "Aucune pièce d'identité enregistrée.")))
            .Produces<KycView>()
            .ProducesProblem(StatusCodes.Status404NotFound);

        group.MapPut("/", async (KycInput input, ClaimsPrincipal user, KycService service, CancellationToken ct) =>
            {
                var result = await service.UpsertAsync(user.UserId(), input, ct);
                return result.IsSuccess ? Results.Ok(result.Value) : ProblemResults.From(result.Error);
            })
            .Produces<KycView>()
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status409Conflict);

        // Bearer-token API (no cookie authentication): CSRF protection does not apply to these form posts.
        group.MapPut("/documents/{side}", async (string side, IFormFile file, ClaimsPrincipal user, KycService service, CancellationToken ct) =>
            {
                if (!Enum.TryParse<DocumentSide>(side, ignoreCase: true, out var parsed) || !Enum.IsDefined(parsed))
                {
                    return ProblemResults.From(PartnerErrors.Invalid("Côté inconnu (front, back ou selfie)."));
                }

                if (file.Length > ImageProcessor.MaxUploadBytes)
                {
                    return ProblemResults.From(new Error("file.too_large", "Le fichier dépasse 10 Mo."));
                }

                await using var stream = file.OpenReadStream();
                var result = await service.UploadDocumentAsync(user.UserId(), parsed, stream, ct);
                return result.IsSuccess ? Results.Ok(result.Value) : ProblemResults.From(result.Error);
            })
            .DisableAntiforgery()
            .WithMetadata(new RequestSizeLimitAttribute(PartnerEndpoints.UploadRequestLimit))
            .Accepts<IFormFile>("multipart/form-data")
            .Produces<KycView>()
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .ProducesProblem(StatusCodes.Status413PayloadTooLarge);

        return app;
    }
}
