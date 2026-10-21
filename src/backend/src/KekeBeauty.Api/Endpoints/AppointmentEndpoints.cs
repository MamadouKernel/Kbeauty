using System.Security.Claims;
using KekeBeauty.Api.Http;
using KekeBeauty.Infrastructure.Scheduling;
using Microsoft.AspNetCore.Mvc;

namespace KekeBeauty.Api.Endpoints;

/// <summary>Public availability, client booking and "my appointments" (PRD RDV-01 to RDV-06).</summary>
public static class AppointmentEndpoints
{
    public const string BookingRateLimitPolicy = "booking";

    public static IEndpointRouteBuilder MapAppointmentEndpoints(this IEndpointRouteBuilder app)
    {
        // Public: the booking page shows availability before sign-in.
        var establishments = app.MapGroup("/api/v1/establishments").WithTags("Booking").AllowAnonymous();

        establishments.MapGet("/{slug}/booking", async (string slug, AvailabilityService service, CancellationToken ct) =>
                await service.GetInfoAsync(slug, ct) is { } info ? Results.Ok(info) : ProblemResults.From(SchedulingErrors.BookingClosed))
            .Produces<BookingInfo>()
            .ProducesProblem(StatusCodes.Status409Conflict);

        establishments.MapGet("/{id:guid}/availability", async (
                Guid id,
                [FromQuery] DateOnly from,
                [FromQuery] DateOnly to,
                [FromQuery] Guid[] serviceIds,
                [FromQuery] Guid? resourceId,
                AvailabilityService service,
                CancellationToken ct) =>
            {
                var result = await service.GetAvailabilityAsync(id, from, to, serviceIds, resourceId, ct);
                return result.IsSuccess ? Results.Ok(result.Value) : ProblemResults.From(result.Error);
            })
            .RequireRateLimiting(BookingRateLimitPolicy)
            .Produces<AvailabilityResponse>()
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict);

        var appointments = app.MapGroup("/api/v1/appointments").WithTags("Booking").RequireAuthorization();

        appointments.MapPost("/", async (BookingRequest request, ClaimsPrincipal user, BookingService service, CancellationToken ct) =>
            {
                var result = await service.CreateAsync(user.UserId(), request, ct);
                return result.IsSuccess
                    ? Results.Created($"/api/v1/me/appointments#{result.Value.Id}", result.Value)
                    : ProblemResults.From(result.Error);
            })
            .RequireRateLimiting(BookingRateLimitPolicy)
            .Produces<AppointmentDto>(StatusCodes.Status201Created)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict);

        appointments.MapPost("/{id:guid}/cancel", async (Guid id, CancelByClientInput? input, ClaimsPrincipal user, BookingService service, CancellationToken ct) =>
                Respond(await service.CancelAsync(user.UserId(), id, input?.AcceptLateFee ?? false, ct)))
            .Produces<AppointmentDto>()
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict);

        appointments.MapPost("/{id:guid}/reschedule", async (Guid id, RescheduleByClientInput input, ClaimsPrincipal user, BookingService service, CancellationToken ct) =>
                Respond(await service.RescheduleAsync(user.UserId(), id, input.StartsAt, ct)))
            .Produces<AppointmentDto>()
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict);

        appointments.MapPost("/{id:guid}/late", async (Guid id, LateInput input, ClaimsPrincipal user, BookingService service, CancellationToken ct) =>
                Respond(await service.SignalLateAsync(user.UserId(), id, input.Minutes, ct)))
            .Produces<AppointmentDto>()
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict);

        appointments.MapPost("/{id:guid}/pay", async (Guid id, ClaimsPrincipal user, BookingService service, CancellationToken ct) =>
                Respond(await service.ResumePaymentAsync(user.UserId(), id, ct)))
            .Produces<AppointmentDto>()
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .ProducesProblem(StatusCodes.Status502BadGateway);

        appointments.MapPost("/{id:guid}/reschedule/accept", async (Guid id, ClaimsPrincipal user, BookingService service, CancellationToken ct) =>
                Respond(await service.AcceptRescheduleAsync(user.UserId(), id, ct)))
            .Produces<AppointmentDto>()
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict);

        appointments.MapPost("/{id:guid}/reschedule/decline", async (Guid id, ClaimsPrincipal user, BookingService service, CancellationToken ct) =>
                Respond(await service.DeclineRescheduleAsync(user.UserId(), id, ct)))
            .Produces<AppointmentDto>()
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict);

        appointments.MapGet("/{id:guid}/pass", async (Guid id, ClaimsPrincipal user, PassService service, CancellationToken ct) =>
            {
                var result = await service.IssueAsync(user.UserId(), id, ct);
                return result.IsSuccess ? Results.Ok(result.Value) : ProblemResults.From(result.Error);
            })
            .Produces<PassDto>()
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status404NotFound);

        appointments.MapGet("/{id:guid}/calendar.ics", async (Guid id, ClaimsPrincipal user, BookingService service, CancellationToken ct) =>
            {
                var result = await service.CalendarIcsAsync(user.UserId(), id, ct);
                return result.IsSuccess
                    ? Results.File(System.Text.Encoding.UTF8.GetBytes(result.Value), "text/calendar; charset=utf-8", $"keke-beauty-rdv-{id:N}.ics")
                    : ProblemResults.From(result.Error);
            })
            .Produces(StatusCodes.Status200OK, contentType: "text/calendar")
            .ProducesProblem(StatusCodes.Status404NotFound);

        appointments.MapPost("/{id:guid}/tip", async (Guid id, TipInput input, ClaimsPrincipal user, KekeBeauty.Infrastructure.Billing.TipPaymentService service, CancellationToken ct) =>
            {
                var result = await service.TipAsync(user.UserId(), id, input.Amount, ct);
                return result.IsSuccess ? Results.Ok(result.Value) : ProblemResults.From(result.Error);
            })
            .Produces<KekeBeauty.Infrastructure.Billing.TipResultDto>()
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .ProducesProblem(StatusCodes.Status502BadGateway);

        app.MapGet("/api/v1/me/appointments", async (ClaimsPrincipal user, BookingService service, CancellationToken ct) =>
                Results.Ok(await service.ListMineAsync(user.UserId(), ct)))
            .WithTags("Booking")
            .RequireAuthorization()
            .Produces<IReadOnlyList<AppointmentDto>>();

        return app;
    }

    private static IResult Respond(KekeBeauty.SharedKernel.Result<AppointmentDto> result) =>
        result.IsSuccess ? Results.Ok(result.Value) : ProblemResults.From(result.Error);
}
