using System.Security.Claims;
using KekeBeauty.Api.Http;
using KekeBeauty.Infrastructure.Billing;
using KekeBeauty.Infrastructure.Scheduling;
using KekeBeauty.SharedKernel;
using Microsoft.AspNetCore.Mvc;

namespace KekeBeauty.Api.Endpoints;

/// <summary>Collaborator portal: today's planning, service timer, client technical notes and commissions (V1 parity lot 3).</summary>
public static class TeamEndpoints
{
    public const string StaffPolicy = "Staff";

    public static IEndpointRouteBuilder MapTeamEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1/team").WithTags("Team").RequireAuthorization(StaffPolicy);

        group.MapGet("/me", async (ClaimsPrincipal user, TeamService service, CancellationToken ct) =>
                Results.Ok(await service.MembershipsAsync(user.UserId(), ct)))
            .Produces<IReadOnlyList<TeamMembershipDto>>();

        group.MapGet("/planning", async ([FromQuery] DateOnly? date, ClaimsPrincipal user, TeamService service, CancellationToken ct) =>
                Respond(await service.PlanningAsync(user.UserId(), date, ct)))
            .Produces<TeamPlanningDto>().ProducesProblem(StatusCodes.Status404NotFound);

        group.MapPost("/appointments/{id:guid}/start", async (Guid id, ClaimsPrincipal user, TeamService service, CancellationToken ct) =>
                Respond(await service.StartAsync(user.UserId(), id, ct)))
            .Produces<TeamAppointmentDto>().ProducesProblem(StatusCodes.Status404NotFound).ProducesProblem(StatusCodes.Status409Conflict);

        group.MapPost("/appointments/{id:guid}/finish", async (Guid id, ClaimsPrincipal user, TeamService service, CancellationToken ct) =>
                Respond(await service.FinishAsync(user.UserId(), id, ct)))
            .Produces<TeamAppointmentDto>().ProducesProblem(StatusCodes.Status404NotFound).ProducesProblem(StatusCodes.Status409Conflict);

        group.MapGet("/establishments/{establishmentId:guid}/clients/{clientId:guid}/note", async (Guid establishmentId, Guid clientId, ClaimsPrincipal user, TeamService service, CancellationToken ct) =>
                Respond(await service.GetNoteAsync(user.UserId(), establishmentId, clientId, ct)))
            .Produces<TechnicalNoteDto>().ProducesProblem(StatusCodes.Status404NotFound);

        group.MapPut("/establishments/{establishmentId:guid}/clients/{clientId:guid}/note", async (Guid establishmentId, Guid clientId, TechnicalNoteInput input, ClaimsPrincipal user, TeamService service, CancellationToken ct) =>
                Respond(await service.SaveNoteAsync(user.UserId(), establishmentId, clientId, input, ct)))
            .Produces<TechnicalNoteDto>().ProducesProblem(StatusCodes.Status400BadRequest).ProducesProblem(StatusCodes.Status404NotFound);

        group.MapGet("/commissions", async ([FromQuery] DateOnly? from, [FromQuery] DateOnly? to, ClaimsPrincipal user, CommissionService service, CancellationToken ct) =>
                Respond(await service.ForCollaboratorAsync(user.UserId(), from, to, ct)))
            .Produces<IReadOnlyList<CommissionSummaryDto>>().ProducesProblem(StatusCodes.Status400BadRequest).ProducesProblem(StatusCodes.Status404NotFound);

        group.MapGet("/payout-account", async (ClaimsPrincipal user, CollaboratorPayoutService service, CancellationToken ct) =>
                Results.Ok(await service.GetAccountAsync(user.UserId(), ct)))
            .Produces<CollaboratorAccountView>();

        group.MapPut("/payout-account", async (CollaboratorAccountInput input, ClaimsPrincipal user, CollaboratorPayoutService service, CancellationToken ct) =>
                Respond(await service.SaveAccountAsync(user.UserId(), input, ct)))
            .Produces<CollaboratorAccountView>().ProducesProblem(StatusCodes.Status400BadRequest);

        group.MapGet("/payouts", async (ClaimsPrincipal user, CollaboratorPayoutService service, CancellationToken ct) =>
                Results.Ok(await service.MineAsync(user.UserId(), ct)))
            .Produces<MyPayoutsView>();

        return app;
    }

    /// <summary>Owner side of the team, mounted under the partner group (role PartnerOwner).</summary>
    public static void MapOwnerTeam(RouteGroupBuilder group)
    {
        MapOwnerPayouts(group);
        const string team = "/establishments/{id:guid}/team";

        group.MapGet(team, async (Guid id, ClaimsPrincipal user, TeamService service, CancellationToken ct) =>
                Respond(await service.ListAsync(user.UserId(), id, ct)))
            .Produces<IReadOnlyList<TeamMemberDto>>().ProducesProblem(StatusCodes.Status404NotFound);

        group.MapPut(team + "/{resourceId:guid}", async (Guid id, Guid resourceId, TeamAssignmentInput input, ClaimsPrincipal user, TeamService service, CancellationToken ct) =>
                Respond(await service.AssignAsync(user.UserId(), id, resourceId, input, ct)))
            .Produces<TeamMemberDto>().ProducesProblem(StatusCodes.Status400BadRequest).ProducesProblem(StatusCodes.Status404NotFound);

        group.MapDelete(team + "/{resourceId:guid}", async (Guid id, Guid resourceId, ClaimsPrincipal user, TeamService service, CancellationToken ct) =>
                PartnerEndpoints.NoContentOr(await service.UnassignAsync(user.UserId(), id, resourceId, ct)))
            .Produces(StatusCodes.Status204NoContent).ProducesProblem(StatusCodes.Status404NotFound);

        group.MapGet("/establishments/{id:guid}/commissions", async (Guid id, [FromQuery] DateOnly? from, [FromQuery] DateOnly? to, ClaimsPrincipal user, CommissionService service, CancellationToken ct) =>
                Respond(await service.ForOwnerAsync(user.UserId(), id, from, to, ct)))
            .Produces<IReadOnlyList<CommissionSummaryDto>>().ProducesProblem(StatusCodes.Status400BadRequest).ProducesProblem(StatusCodes.Status404NotFound);
    }

    private static void MapOwnerPayouts(RouteGroupBuilder group)
    {
        group.MapGet("/establishments/{id:guid}/team-payouts", async (Guid id, ClaimsPrincipal user, CollaboratorPayoutService service, CancellationToken ct) =>
                Respond(await service.ViewAsync(user.UserId(), id, ct)))
            .Produces<CollaboratorPayoutsView>().ProducesProblem(StatusCodes.Status404NotFound);

        group.MapPost("/establishments/{id:guid}/team-payouts/{userId:guid}", async (Guid id, Guid userId, ClaimsPrincipal user, CollaboratorPayoutService service, CancellationToken ct) =>
                Respond(await service.PayAsync(user.UserId(), id, userId, ct)))
            .Produces<CollaboratorPayoutDto>()
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .ProducesProblem(StatusCodes.Status502BadGateway);
    }

    private static IResult Respond<T>(Result<T> result) => result.IsSuccess ? Results.Ok(result.Value) : ProblemResults.From(result.Error);
}
