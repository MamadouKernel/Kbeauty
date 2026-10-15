using System.Security.Claims;
using KekeBeauty.Api.Http;
using KekeBeauty.Infrastructure.Scheduling;
using KekeBeauty.SharedKernel;
using Microsoft.AspNetCore.Mvc;

namespace KekeBeauty.Api.Endpoints;

/// <summary>Partner agenda: appointments, resources, closures, settings and statistics (PRD AGD-01 to AGD-08).</summary>
public static class AgendaEndpoints
{
    public static IEndpointRouteBuilder MapAgendaEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1/pro").WithTags("Agenda").RequireAuthorization(PartnerEndpoints.PartnerPolicy);

        MapSettings(group);
        MapPayments(group);
        MapResources(group);
        MapAppointments(group);
        return app;
    }

    private static IResult Respond<T>(Result<T> result) => result.IsSuccess ? Results.Ok(result.Value) : ProblemResults.From(result.Error);

    private static void MapSettings(RouteGroupBuilder group)
    {
        group.MapGet("/establishments/{id:guid}/booking-settings", async (Guid id, ClaimsPrincipal user, AgendaService service, CancellationToken ct) =>
                Respond(await service.GetSettingsAsync(user.UserId(), id, ct)))
            .Produces<BookingSettingsDto>().ProducesProblem(StatusCodes.Status404NotFound);

        group.MapPut("/establishments/{id:guid}/booking-settings", async (Guid id, BookingSettingsDto input, ClaimsPrincipal user, AgendaService service, CancellationToken ct) =>
                Respond(await service.UpdateSettingsAsync(user.UserId(), id, input, ct)))
            .Produces<BookingSettingsDto>().ProducesProblem(StatusCodes.Status400BadRequest).ProducesProblem(StatusCodes.Status404NotFound);

        group.MapGet("/establishments/{id:guid}/stats", async (Guid id, [FromQuery] DateOnly? from, [FromQuery] DateOnly? to, ClaimsPrincipal user, AgendaService service, TimeProvider time, CancellationToken ct) =>
            {
                var today = DateOnly.FromDateTime(time.GetUtcNow().UtcDateTime);
                return Respond(await service.StatsAsync(user.UserId(), id, from ?? today.AddDays(-29), to ?? today, ct));
            })
            .Produces<StatsDto>().ProducesProblem(StatusCodes.Status400BadRequest).ProducesProblem(StatusCodes.Status404NotFound);
    }

    private static void MapPayments(RouteGroupBuilder group)
    {
        group.MapGet("/establishments/{id:guid}/payment-settings", async (Guid id, ClaimsPrincipal user, KekeBeauty.Infrastructure.Billing.PayoutService service, CancellationToken ct) =>
                Respond(await service.GetSettingsAsync(user.UserId(), id, ct)))
            .Produces<KekeBeauty.Infrastructure.Billing.PaymentSettingsDto>().ProducesProblem(StatusCodes.Status404NotFound);

        group.MapPut("/establishments/{id:guid}/payment-settings", async (Guid id, KekeBeauty.Infrastructure.Billing.PaymentSettingsDto input, ClaimsPrincipal user, KekeBeauty.Infrastructure.Billing.PayoutService service, CancellationToken ct) =>
                Respond(await service.UpdateSettingsAsync(user.UserId(), id, input, ct)))
            .Produces<KekeBeauty.Infrastructure.Billing.PaymentSettingsDto>().ProducesProblem(StatusCodes.Status400BadRequest).ProducesProblem(StatusCodes.Status404NotFound);

        group.MapGet("/establishments/{id:guid}/payments", async (Guid id, ClaimsPrincipal user, KekeBeauty.Infrastructure.Billing.PayoutService service, CancellationToken ct) =>
                Respond(await service.ViewAsync(user.UserId(), id, ct)))
            .Produces<KekeBeauty.Infrastructure.Billing.PartnerPaymentsView>().ProducesProblem(StatusCodes.Status404NotFound);
    }

    private static void MapResources(RouteGroupBuilder group)
    {
        const string resources = "/establishments/{id:guid}/resources";
        const string timeOff = "/establishments/{id:guid}/time-off";

        group.MapGet(resources, async (Guid id, ClaimsPrincipal user, ResourceService service, CancellationToken ct) =>
                Respond(await service.ListAsync(user.UserId(), id, ct)))
            .Produces<IReadOnlyList<ResourceDto>>().ProducesProblem(StatusCodes.Status404NotFound);

        group.MapPost(resources, async (Guid id, ResourceInput input, ClaimsPrincipal user, ResourceService service, CancellationToken ct) =>
                PartnerEndpoints.CreatedOr(await service.CreateAsync(user.UserId(), id, input, ct), $"/api/v1/pro/establishments/{id}/resources"))
            .Produces<CreatedId>(StatusCodes.Status201Created).ProducesProblem(StatusCodes.Status400BadRequest);

        group.MapPut(resources + "/{resourceId:guid}", async (Guid id, Guid resourceId, ResourceInput input, ClaimsPrincipal user, ResourceService service, CancellationToken ct) =>
                PartnerEndpoints.NoContentOr(await service.UpdateAsync(user.UserId(), id, resourceId, input, ct)))
            .Produces(StatusCodes.Status204NoContent).ProducesProblem(StatusCodes.Status400BadRequest).ProducesProblem(StatusCodes.Status404NotFound);

        group.MapDelete(resources + "/{resourceId:guid}", async (Guid id, Guid resourceId, ClaimsPrincipal user, ResourceService service, CancellationToken ct) =>
                PartnerEndpoints.NoContentOr(await service.DeleteAsync(user.UserId(), id, resourceId, ct)))
            .Produces(StatusCodes.Status204NoContent).ProducesProblem(StatusCodes.Status404NotFound);

        group.MapGet(timeOff, async (Guid id, ClaimsPrincipal user, ResourceService service, CancellationToken ct) =>
                Respond(await service.ListTimeOffAsync(user.UserId(), id, ct)))
            .Produces<IReadOnlyList<TimeOffDto>>().ProducesProblem(StatusCodes.Status404NotFound);

        group.MapPost(timeOff, async (Guid id, TimeOffInput input, ClaimsPrincipal user, ResourceService service, CancellationToken ct) =>
                PartnerEndpoints.CreatedOr(await service.CreateTimeOffAsync(user.UserId(), id, input, ct), $"/api/v1/pro/establishments/{id}/time-off"))
            .Produces<CreatedId>(StatusCodes.Status201Created).ProducesProblem(StatusCodes.Status400BadRequest);

        group.MapDelete(timeOff + "/{timeOffId:guid}", async (Guid id, Guid timeOffId, ClaimsPrincipal user, ResourceService service, CancellationToken ct) =>
                PartnerEndpoints.NoContentOr(await service.DeleteTimeOffAsync(user.UserId(), id, timeOffId, ct)))
            .Produces(StatusCodes.Status204NoContent).ProducesProblem(StatusCodes.Status404NotFound);
    }

    private static void MapAppointments(RouteGroupBuilder group)
    {
        group.MapGet("/establishments/{id:guid}/appointments", async (
                Guid id,
                [FromQuery] string? status,
                [FromQuery] DateTimeOffset? from,
                [FromQuery] DateTimeOffset? to,
                ClaimsPrincipal user,
                AgendaService service,
                CancellationToken ct) =>
                Respond(await service.ListAsync(user.UserId(), id, status, from, to, ct)))
            .Produces<IReadOnlyList<PartnerAppointmentDto>>().ProducesProblem(StatusCodes.Status400BadRequest).ProducesProblem(StatusCodes.Status404NotFound);

        group.MapPost("/appointments/{id:guid}/confirm", async (Guid id, ClaimsPrincipal user, AgendaService service, CancellationToken ct) =>
                Respond(await service.ConfirmAsync(user.UserId(), id, ct)))
            .Produces<PartnerAppointmentDto>().ProducesProblem(StatusCodes.Status404NotFound).ProducesProblem(StatusCodes.Status409Conflict);

        group.MapPost("/appointments/{id:guid}/reject", async (Guid id, RejectInput input, ClaimsPrincipal user, AgendaService service, CancellationToken ct) =>
                Respond(await service.RejectAsync(user.UserId(), id, input.Reason, ct)))
            .Produces<PartnerAppointmentDto>().ProducesProblem(StatusCodes.Status404NotFound).ProducesProblem(StatusCodes.Status409Conflict);

        group.MapPost("/appointments/{id:guid}/propose-reschedule", async (Guid id, RescheduleProposal input, ClaimsPrincipal user, AgendaService service, CancellationToken ct) =>
                Respond(await service.ProposeRescheduleAsync(user.UserId(), id, input, ct)))
            .Produces<PartnerAppointmentDto>().ProducesProblem(StatusCodes.Status404NotFound).ProducesProblem(StatusCodes.Status409Conflict);

        group.MapPost("/appointments/{id:guid}/cancel", async (Guid id, CancelInput input, ClaimsPrincipal user, AgendaService service, CancellationToken ct) =>
                Respond(await service.CancelAsync(user.UserId(), id, input.Reason, ct)))
            .Produces<PartnerAppointmentDto>().ProducesProblem(StatusCodes.Status404NotFound).ProducesProblem(StatusCodes.Status409Conflict);

        group.MapPost("/appointments/{id:guid}/mark-paid", async (Guid id, MarkPaidInput input, ClaimsPrincipal user, AgendaService service, CancellationToken ct) =>
                Respond(await service.MarkPaidAsync(user.UserId(), id, input, ct)))
            .Produces<PartnerAppointmentDto>().ProducesProblem(StatusCodes.Status400BadRequest).ProducesProblem(StatusCodes.Status404NotFound);

        group.MapPost("/appointments/{id:guid}/mark-unpaid", async (Guid id, ClaimsPrincipal user, AgendaService service, CancellationToken ct) =>
                Respond(await service.MarkUnpaidAsync(user.UserId(), id, ct)))
            .Produces<PartnerAppointmentDto>().ProducesProblem(StatusCodes.Status404NotFound);

        group.MapPost("/appointments/{id:guid}/no-show", async (Guid id, ClaimsPrincipal user, AgendaService service, CancellationToken ct) =>
                Respond(await service.NoShowAsync(user.UserId(), id, ct)))
            .Produces<PartnerAppointmentDto>().ProducesProblem(StatusCodes.Status404NotFound).ProducesProblem(StatusCodes.Status409Conflict);
    }
}
