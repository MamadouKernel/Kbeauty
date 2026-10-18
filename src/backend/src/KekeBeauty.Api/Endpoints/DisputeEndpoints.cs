using System.Security.Claims;
using KekeBeauty.Api.Http;
using KekeBeauty.Infrastructure.Admin;
using KekeBeauty.Infrastructure.Scheduling;
using KekeBeauty.Modules.Identity.Domain;
using KekeBeauty.SharedKernel;
using Microsoft.AspNetCore.Mvc;

namespace KekeBeauty.Api.Endpoints;

/// <summary>Keke Protect: claims filed by clients, answered by establishments, decided by administrators (V1 parity lot 4).</summary>
public static class DisputeEndpoints
{
    public static IEndpointRouteBuilder MapDisputeEndpoints(this IEndpointRouteBuilder app)
    {
        var client = app.MapGroup("/api/v1").WithTags("Disputes").RequireAuthorization();

        client.MapPost("/disputes", async (OpenDisputeInput input, ClaimsPrincipal user, DisputeService service, CancellationToken ct) =>
            {
                var result = await service.OpenAsync(user.UserId(), input, ct);
                return result.IsSuccess ? Results.Created("/api/v1/me/disputes", result.Value) : ProblemResults.From(result.Error);
            })
            .Produces<DisputeDto>(StatusCodes.Status201Created)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict);

        client.MapGet("/me/disputes", async (ClaimsPrincipal user, DisputeService service, CancellationToken ct) =>
                Results.Ok(await service.MineAsync(user.UserId(), ct)))
            .Produces<IReadOnlyList<DisputeDto>>();

        var partner = app.MapGroup("/api/v1/pro").WithTags("Disputes").RequireAuthorization(PartnerEndpoints.PartnerPolicy);

        partner.MapGet("/establishments/{id:guid}/disputes", async (Guid id, ClaimsPrincipal user, DisputeService service, CancellationToken ct) =>
                Respond(await service.ForEstablishmentAsync(user.UserId(), id, ct)))
            .Produces<IReadOnlyList<DisputeDto>>().ProducesProblem(StatusCodes.Status404NotFound);

        partner.MapPost("/disputes/{id:guid}/respond", async (Guid id, DisputeResponseInput input, ClaimsPrincipal user, DisputeService service, CancellationToken ct) =>
                Respond(await service.RespondAsync(user.UserId(), id, input, ct)))
            .Produces<DisputeDto>().ProducesProblem(StatusCodes.Status400BadRequest).ProducesProblem(StatusCodes.Status404NotFound).ProducesProblem(StatusCodes.Status409Conflict);

        var admin = app.MapGroup("/api/v1/admin/disputes").WithTags("Admin").RequireAuthorization(AdminReviewEndpoints.AdminPolicy);

        admin.MapGet("/", async ([FromQuery] string? status, DisputeService service, CancellationToken ct) =>
                Respond(await service.ListAsync(status, ct)))
            .Produces<IReadOnlyList<DisputeDto>>().ProducesProblem(StatusCodes.Status400BadRequest);

        admin.MapPost("/{id:guid}/resolve", async (Guid id, DisputeResolveInput input, ClaimsPrincipal user, HttpContext http, DisputeService service, CancellationToken ct) =>
            {
                var context = new AdminContext(user.UserId(), user.IsInRole(AppRoles.SuperAdmin), http.Connection.RemoteIpAddress?.ToString());
                return Respond(await service.ResolveAsync(context, id, input, ct));
            })
            .Produces<DisputeDto>().ProducesProblem(StatusCodes.Status400BadRequest).ProducesProblem(StatusCodes.Status404NotFound).ProducesProblem(StatusCodes.Status409Conflict);

        return app;
    }

    private static IResult Respond<T>(Result<T> result) => result.IsSuccess ? Results.Ok(result.Value) : ProblemResults.From(result.Error);
}
