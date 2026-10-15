using System.Security.Claims;
using Microsoft.AspNetCore.Mvc;
using KekeBeauty.Api.Http;
using KekeBeauty.Infrastructure.Partner;
using KekeBeauty.SharedKernel;

namespace KekeBeauty.Api.Endpoints;

public sealed record CreatedId(Guid Id);

/// <summary>Partner area (<c>/api/v1/pro</c>): establishments, hours, services and gallery. Owner role required.</summary>
public static class PartnerEndpoints
{
    public const string PartnerPolicy = "Partner";

    /// <summary>A little above the 10 MB image limit (multipart framing); anything larger is refused before it is read.</summary>
    public const long UploadRequestLimit = 12 * 1024 * 1024;
    public const long VideoRequestLimit = 64 * 1024 * 1024;

    public static IEndpointRouteBuilder MapPartnerEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1/pro").WithTags("Partner").RequireAuthorization(PartnerPolicy);

        MapEstablishments(group);
        MapCatalog(group);
        MapMedia(group);
        MapAcceptedPayments(group);
        return app;
    }

    internal static Guid UserId(this ClaimsPrincipal principal) => Guid.Parse(principal.FindFirstValue("sub")!);

    internal static IResult NoContentOr(Result result) => result.IsSuccess ? Results.NoContent() : ProblemResults.From(result.Error);

    internal static IResult CreatedOr(Result<Guid> result, string location) =>
        result.IsSuccess ? Results.Created($"{location}/{result.Value}", new CreatedId(result.Value)) : ProblemResults.From(result.Error);

    private static void MapEstablishments(RouteGroupBuilder group)
    {
        group.MapGet("/establishments", async (ClaimsPrincipal user, PartnerEstablishmentService service, CancellationToken ct) =>
                Results.Ok(await service.ListAsync(user.UserId(), ct)))
            .Produces<IReadOnlyList<PartnerEstablishmentSummary>>();

        group.MapPost("/establishments", async (EstablishmentInput input, ClaimsPrincipal user, PartnerEstablishmentService service, CancellationToken ct) =>
                CreatedOr(await service.CreateAsync(user.UserId(), input, ct), "/api/v1/pro/establishments"))
            .Produces<CreatedId>(StatusCodes.Status201Created)
            .ProducesProblem(StatusCodes.Status400BadRequest);

        group.MapGet("/establishments/{id:guid}", async (Guid id, ClaimsPrincipal user, PartnerEstablishmentService service, CancellationToken ct) =>
                await service.GetAsync(user.UserId(), id, ct) is { } detail ? Results.Ok(detail) : ProblemResults.From(PartnerErrors.NotFound))
            .Produces<PartnerEstablishmentDetail>()
            .ProducesProblem(StatusCodes.Status404NotFound);

        group.MapPut("/establishments/{id:guid}", async (Guid id, EstablishmentInput input, ClaimsPrincipal user, PartnerEstablishmentService service, CancellationToken ct) =>
                NoContentOr(await service.UpdateAsync(user.UserId(), id, input, ct)))
            .Produces(StatusCodes.Status204NoContent)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict);

        group.MapPut("/establishments/{id:guid}/opening-hours", async (Guid id, List<OpeningHourInput> hours, ClaimsPrincipal user, PartnerEstablishmentService service, CancellationToken ct) =>
                NoContentOr(await service.SetOpeningHoursAsync(user.UserId(), id, hours, ct)))
            .Produces(StatusCodes.Status204NoContent)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict);

        group.MapPost("/establishments/{id:guid}/submit", async (Guid id, ClaimsPrincipal user, PartnerEstablishmentService service, CancellationToken ct) =>
            {
                var result = await service.SubmitAsync(user.UserId(), id, ct);
                return result.IsSuccess ? Results.Ok(result.Value) : ProblemResults.From(result.Error);
            })
            .Produces<ReviewInfo>()
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict);
    }

    private static void MapCatalog(RouteGroupBuilder group)
    {
        const string groups = "/establishments/{id:guid}/service-groups";
        const string services = "/establishments/{id:guid}/services";

        group.MapPost(groups, async (Guid id, GroupInput input, ClaimsPrincipal user, PartnerCatalogService service, CancellationToken ct) =>
                CreatedOr(await service.AddGroupAsync(user.UserId(), id, input, ct), $"/api/v1/pro/establishments/{id}/service-groups"))
            .Produces<CreatedId>(StatusCodes.Status201Created).ProducesProblem(StatusCodes.Status400BadRequest);

        group.MapPut(groups + "/{groupId:guid}", async (Guid id, Guid groupId, GroupInput input, ClaimsPrincipal user, PartnerCatalogService service, CancellationToken ct) =>
                NoContentOr(await service.RenameGroupAsync(user.UserId(), id, groupId, input, ct)))
            .Produces(StatusCodes.Status204NoContent).ProducesProblem(StatusCodes.Status400BadRequest).ProducesProblem(StatusCodes.Status404NotFound);

        group.MapDelete(groups + "/{groupId:guid}", async (Guid id, Guid groupId, ClaimsPrincipal user, PartnerCatalogService service, CancellationToken ct) =>
                NoContentOr(await service.DeleteGroupAsync(user.UserId(), id, groupId, ct)))
            .Produces(StatusCodes.Status204NoContent).ProducesProblem(StatusCodes.Status404NotFound);

        group.MapPut(groups + "/order", async (Guid id, OrderInput input, ClaimsPrincipal user, PartnerCatalogService service, CancellationToken ct) =>
                NoContentOr(await service.ReorderGroupsAsync(user.UserId(), id, input, ct)))
            .Produces(StatusCodes.Status204NoContent).ProducesProblem(StatusCodes.Status400BadRequest);

        group.MapPost(services, async (Guid id, ServiceInput input, ClaimsPrincipal user, PartnerCatalogService service, CancellationToken ct) =>
                CreatedOr(await service.AddServiceAsync(user.UserId(), id, input, ct), $"/api/v1/pro/establishments/{id}/services"))
            .Produces<CreatedId>(StatusCodes.Status201Created).ProducesProblem(StatusCodes.Status400BadRequest);

        group.MapPut(services + "/{serviceId:guid}", async (Guid id, Guid serviceId, ServiceInput input, ClaimsPrincipal user, PartnerCatalogService service, CancellationToken ct) =>
                NoContentOr(await service.UpdateServiceAsync(user.UserId(), id, serviceId, input, ct)))
            .Produces(StatusCodes.Status204NoContent).ProducesProblem(StatusCodes.Status400BadRequest).ProducesProblem(StatusCodes.Status404NotFound);

        group.MapDelete(services + "/{serviceId:guid}", async (Guid id, Guid serviceId, ClaimsPrincipal user, PartnerCatalogService service, CancellationToken ct) =>
                NoContentOr(await service.DeleteServiceAsync(user.UserId(), id, serviceId, ct)))
            .Produces(StatusCodes.Status204NoContent).ProducesProblem(StatusCodes.Status404NotFound);

        group.MapPut(services + "/order", async (Guid id, OrderInput input, ClaimsPrincipal user, PartnerCatalogService service, CancellationToken ct) =>
                NoContentOr(await service.ReorderServicesAsync(user.UserId(), id, input, ct)))
            .Produces(StatusCodes.Status204NoContent).ProducesProblem(StatusCodes.Status400BadRequest);
    }

    private static void MapAcceptedPayments(RouteGroupBuilder group)
    {
        // How clients pay the establishment for the service (cash, mobile money sent straight to it): the platform is not involved.
        group.MapGet("/establishments/{id:guid}/accepted-payments", async (Guid id, ClaimsPrincipal user, AcceptedPaymentsService service, CancellationToken ct) =>
            {
                var result = await service.GetAsync(user.UserId(), id, ct);
                return result.IsSuccess ? Results.Ok(result.Value) : ProblemResults.From(result.Error);
            })
            .Produces<AcceptedPaymentsDto>().ProducesProblem(StatusCodes.Status404NotFound);

        group.MapPut("/establishments/{id:guid}/accepted-payments", async (Guid id, AcceptedPaymentsInput input, ClaimsPrincipal user, AcceptedPaymentsService service, CancellationToken ct) =>
            {
                var result = await service.UpdateAsync(user.UserId(), id, input, ct);
                return result.IsSuccess ? Results.Ok(result.Value) : ProblemResults.From(result.Error);
            })
            .Produces<AcceptedPaymentsDto>().ProducesProblem(StatusCodes.Status400BadRequest).ProducesProblem(StatusCodes.Status404NotFound);
    }

    private static void MapMedia(RouteGroupBuilder group)
    {
        const string media = "/establishments/{id:guid}/media";

        // Bearer-token API (no cookie authentication): CSRF protection does not apply to these form posts.
        group.MapPost(media, async (Guid id, IFormFile file, ClaimsPrincipal user, PartnerMediaService service, CancellationToken ct) =>
            {
                if (file.Length > ImageProcessor.MaxUploadBytes)
                {
                    return ProblemResults.From(new Error("file.too_large", "Le fichier dépasse 10 Mo."));
                }

                await using var stream = file.OpenReadStream();
                var result = await service.AddPhotoAsync(user.UserId(), id, stream, ct);
                return result.IsSuccess
                    ? Results.Created($"/api/v1/pro/establishments/{id}/media/{result.Value.Id}", result.Value)
                    : ProblemResults.From(result.Error);
            })
            .DisableAntiforgery()
            .WithMetadata(new RequestSizeLimitAttribute(UploadRequestLimit))
            .Accepts<IFormFile>("multipart/form-data")
            .Produces<PartnerMediaItem>(StatusCodes.Status201Created)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .ProducesProblem(StatusCodes.Status413PayloadTooLarge);

        group.MapPost("/establishments/{id:guid}/videos", async (
                Guid id, IFormFile file, IFormFile? poster, [FromForm] int? durationSeconds,
                ClaimsPrincipal user, PartnerMediaService service, CancellationToken ct) =>
            {
                if (poster is { Length: > ImageProcessor.MaxUploadBytes })
                {
                    return ProblemResults.From(new Error("file.too_large", "L'image de couverture dépasse 10 Mo."));
                }

                await using var stream = file.OpenReadStream();
                await using var posterStream = poster?.OpenReadStream();
                var result = await service.AddVideoAsync(user.UserId(), id, stream, posterStream, durationSeconds, ct);
                return result.IsSuccess
                    ? Results.Created($"/api/v1/pro/establishments/{id}/media/{result.Value.Id}", result.Value)
                    : ProblemResults.From(result.Error);
            })
            .DisableAntiforgery()
            .WithMetadata(new RequestSizeLimitAttribute(VideoRequestLimit))
            .Accepts<IFormFile>("multipart/form-data")
            .Produces<PartnerMediaItem>(StatusCodes.Status201Created)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .ProducesProblem(StatusCodes.Status413PayloadTooLarge);

        group.MapDelete(media + "/{mediaId:guid}", async (Guid id, Guid mediaId, ClaimsPrincipal user, PartnerMediaService service, CancellationToken ct) =>
                NoContentOr(await service.DeleteAsync(user.UserId(), id, mediaId, ct)))
            .Produces(StatusCodes.Status204NoContent).ProducesProblem(StatusCodes.Status404NotFound).ProducesProblem(StatusCodes.Status409Conflict);

        group.MapPut(media + "/{mediaId:guid}/cover", async (Guid id, Guid mediaId, ClaimsPrincipal user, PartnerMediaService service, CancellationToken ct) =>
                NoContentOr(await service.SetCoverAsync(user.UserId(), id, mediaId, ct)))
            .Produces(StatusCodes.Status204NoContent).ProducesProblem(StatusCodes.Status404NotFound);

        group.MapPut(media + "/order", async (Guid id, OrderInput input, ClaimsPrincipal user, PartnerMediaService service, CancellationToken ct) =>
                NoContentOr(await service.ReorderAsync(user.UserId(), id, input, ct)))
            .Produces(StatusCodes.Status204NoContent).ProducesProblem(StatusCodes.Status400BadRequest);
    }
}
