using System.Security.Claims;
using System.Threading.RateLimiting;
using KekeBeauty.Api.Http;
using KekeBeauty.Infrastructure.Growth;
using KekeBeauty.SharedKernel;
using Microsoft.AspNetCore.Mvc;

namespace KekeBeauty.Api.Endpoints;

/// <summary>
/// Beyond V1 (lot 7): waiting list, loyalty cards, gift cards, group bookings and "salons near you" recommendations.
/// </summary>
public static class GrowthEndpoints
{
    /// <summary>Typing gift card codes: a person needs a handful of tries, a script guessing codes needs millions.</summary>
    public const string GiftCardRateLimitPolicy = "gift-card-code";

    public static IServiceCollection AddGrowthRateLimits(this IServiceCollection services) =>
        services.AddRateLimiter(limiter => limiter.AddPolicy(GiftCardRateLimitPolicy, context =>
            RateLimitPartition.GetFixedWindowLimiter(
                context.User.FindFirstValue("sub") ?? context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
                _ => new FixedWindowRateLimiterOptions
                {
                    PermitLimit = context.RequestServices.GetRequiredService<IConfiguration>().GetValue("RateLimits:GiftCardCodesPerMinute", 10),
                    Window = TimeSpan.FromMinutes(1),
                })));

    public static IEndpointRouteBuilder MapGrowthEndpoints(this IEndpointRouteBuilder app)
    {
        MapPublic(app);
        MapClient(app);
        MapPartner(app);
        return app;
    }

    private static void MapPublic(IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1").WithTags("Discovery");

        // Open to visitors; a signed-in client gets suggestions shaped by the places she already likes.
        group.MapGet("/recommendations", async ([FromQuery] double? lat, [FromQuery] double? lng, [FromQuery] int? count, ClaimsPrincipal user, RecommendationService service, CancellationToken ct) =>
                Results.Ok(await service.RecommendAsync(OptionalUserId(user), lat, lng, count ?? RecommendationService.DefaultCount, ct)))
            .RequireRateLimiting(EstablishmentEndpoints.EventsRateLimitPolicy)
            .Produces<IReadOnlyList<RecommendationDto>>();

        group.MapGet("/establishments/{slug}/loyalty", async (string slug, LoyaltyService service, CancellationToken ct) =>
                await service.PublicAsync(slug, ct) is { } program ? Results.Ok(program) : Results.NotFound())
            .Produces<PublicLoyaltyDto>()
            .Produces(StatusCodes.Status404NotFound);
    }

    private static void MapClient(IEndpointRouteBuilder app)
    {
        var client = app.MapGroup("/api/v1").WithTags("Client extras").RequireAuthorization();

        client.MapPost("/waitlist", async (JoinWaitlistInput input, ClaimsPrincipal user, WaitlistService service, CancellationToken ct) =>
                Respond(await service.JoinAsync(user.UserId(), input, ct)))
            .Produces<WaitlistEntryDto>().ProducesProblem(StatusCodes.Status400BadRequest).ProducesProblem(StatusCodes.Status404NotFound).ProducesProblem(StatusCodes.Status409Conflict);

        client.MapGet("/me/waitlist", async (ClaimsPrincipal user, WaitlistService service, CancellationToken ct) =>
                Results.Ok(await service.MineAsync(user.UserId(), ct)))
            .Produces<IReadOnlyList<WaitlistEntryDto>>();

        client.MapDelete("/me/waitlist/{id:guid}", async (Guid id, ClaimsPrincipal user, WaitlistService service, CancellationToken ct) =>
            {
                var result = await service.LeaveAsync(user.UserId(), id, ct);
                return result.IsSuccess ? Results.NoContent() : ProblemResults.From(result.Error);
            })
            .Produces(StatusCodes.Status204NoContent).ProducesProblem(StatusCodes.Status404NotFound);

        client.MapGet("/me/loyalty", async (ClaimsPrincipal user, LoyaltyService service, CancellationToken ct) =>
                Results.Ok(await service.MineAsync(user.UserId(), ct)))
            .Produces<IReadOnlyList<LoyaltyCardDto>>();

        client.MapGet("/me/gift-cards", async (ClaimsPrincipal user, GiftCardService service, CancellationToken ct) =>
                Results.Ok(await service.MineAsync(user.UserId(), ct)))
            .Produces<IReadOnlyList<GiftCardDto>>();

        client.MapPost("/me/gift-cards", async (ClaimGiftCardInput input, ClaimsPrincipal user, GiftCardService service, CancellationToken ct) =>
                Respond(await service.ClaimAsync(user.UserId(), input, ct)))
            .RequireRateLimiting(GiftCardRateLimitPolicy)
            .Produces<GiftCardDto>().ProducesProblem(StatusCodes.Status404NotFound).ProducesProblem(StatusCodes.Status409Conflict);

        client.MapPost("/group-bookings", async (GroupBookingInput input, ClaimsPrincipal user, GroupBookingService service, CancellationToken ct) =>
            {
                var result = await service.CreateAsync(user.UserId(), input, ct);
                return result.IsSuccess ? Results.Created("/api/v1/me/group-bookings", result.Value) : ProblemResults.From(result.Error);
            })
            .RequireRateLimiting(AppointmentEndpoints.BookingRateLimitPolicy)
            .Produces<GroupBookingDto>(StatusCodes.Status201Created).ProducesProblem(StatusCodes.Status400BadRequest).ProducesProblem(StatusCodes.Status404NotFound).ProducesProblem(StatusCodes.Status409Conflict);

        client.MapGet("/me/group-bookings", async (ClaimsPrincipal user, GroupBookingService service, CancellationToken ct) =>
                Results.Ok(await service.MineAsync(user.UserId(), ct)))
            .Produces<IReadOnlyList<GroupBookingDto>>();

        client.MapPost("/me/group-bookings/{id:guid}/cancel", async (Guid id, ClaimsPrincipal user, GroupBookingService service, CancellationToken ct) =>
                Respond(await service.CancelAsync(user.UserId(), id, ct)))
            .Produces<GroupBookingDto>().ProducesProblem(StatusCodes.Status404NotFound).ProducesProblem(StatusCodes.Status409Conflict);
    }

    private static void MapPartner(IEndpointRouteBuilder app)
    {
        var partner = app.MapGroup("/api/v1/pro").WithTags("Partner extras").RequireAuthorization(PartnerEndpoints.PartnerPolicy);

        partner.MapGet("/establishments/{id:guid}/waitlist", async (Guid id, ClaimsPrincipal user, WaitlistService service, CancellationToken ct) =>
                Respond(await service.ForEstablishmentAsync(user.UserId(), id, ct)))
            .Produces<IReadOnlyList<WaitlistEntryDto>>().ProducesProblem(StatusCodes.Status404NotFound);

        // Loyalty card: null (204) while the establishment has never set one up.
        partner.MapGet("/establishments/{id:guid}/loyalty", async (Guid id, ClaimsPrincipal user, LoyaltyService service, CancellationToken ct) =>
            {
                var result = await service.GetProgramAsync(user.UserId(), id, ct);
                return result.IsFailure ? ProblemResults.From(result.Error) : result.Value is null ? Results.NoContent() : Results.Ok(result.Value);
            })
            .Produces<LoyaltyProgramDto>().Produces(StatusCodes.Status204NoContent).ProducesProblem(StatusCodes.Status404NotFound);

        partner.MapPut("/establishments/{id:guid}/loyalty", async (Guid id, LoyaltyProgramInput input, ClaimsPrincipal user, LoyaltyService service, CancellationToken ct) =>
                Respond(await service.SaveProgramAsync(user.UserId(), id, input, ct)))
            .Produces<LoyaltyProgramDto>().ProducesProblem(StatusCodes.Status400BadRequest).ProducesProblem(StatusCodes.Status404NotFound);

        partner.MapGet("/establishments/{id:guid}/loyalty/members", async (Guid id, ClaimsPrincipal user, LoyaltyService service, CancellationToken ct) =>
                Respond(await service.MembersAsync(user.UserId(), id, ct)))
            .Produces<IReadOnlyList<LoyaltyMemberDto>>().ProducesProblem(StatusCodes.Status404NotFound);

        partner.MapPost("/establishments/{id:guid}/loyalty/redeem", async (Guid id, LoyaltyRedeemInput input, ClaimsPrincipal user, LoyaltyService service, CancellationToken ct) =>
                Respond(await service.RedeemAsync(user.UserId(), id, input, ct)))
            .Produces<LoyaltyMemberDto>().ProducesProblem(StatusCodes.Status404NotFound).ProducesProblem(StatusCodes.Status409Conflict);

        partner.MapPost("/establishments/{id:guid}/gift-cards", async (Guid id, CreateGiftCardInput input, ClaimsPrincipal user, GiftCardService service, CancellationToken ct) =>
            {
                var result = await service.CreateAsync(user.UserId(), id, input, ct);
                return result.IsSuccess ? Results.Created($"/api/v1/pro/establishments/{id}/gift-cards", result.Value) : ProblemResults.From(result.Error);
            })
            .Produces<GiftCardDto>(StatusCodes.Status201Created).ProducesProblem(StatusCodes.Status400BadRequest).ProducesProblem(StatusCodes.Status404NotFound);

        partner.MapGet("/establishments/{id:guid}/gift-cards", async (Guid id, ClaimsPrincipal user, GiftCardService service, CancellationToken ct) =>
                Respond(await service.ListAsync(user.UserId(), id, ct)))
            .Produces<IReadOnlyList<GiftCardDto>>().ProducesProblem(StatusCodes.Status404NotFound);

        partner.MapGet("/establishments/{id:guid}/gift-cards/lookup", async (Guid id, [FromQuery] string? code, ClaimsPrincipal user, GiftCardService service, CancellationToken ct) =>
                Respond(await service.LookupAsync(user.UserId(), id, code, ct)))
            .RequireRateLimiting(GiftCardRateLimitPolicy)
            .Produces<GiftCardDto>().ProducesProblem(StatusCodes.Status404NotFound);

        partner.MapPost("/gift-cards/{id:guid}/redeem", async (Guid id, RedeemGiftCardInput input, ClaimsPrincipal user, GiftCardService service, CancellationToken ct) =>
                Respond(await service.RedeemAsync(user.UserId(), id, input, ct)))
            .Produces<GiftCardDto>().ProducesProblem(StatusCodes.Status400BadRequest).ProducesProblem(StatusCodes.Status404NotFound).ProducesProblem(StatusCodes.Status409Conflict);

        partner.MapPost("/gift-cards/{id:guid}/cancel", async (Guid id, ClaimsPrincipal user, GiftCardService service, CancellationToken ct) =>
                Respond(await service.CancelAsync(user.UserId(), id, ct)))
            .Produces<GiftCardDto>().ProducesProblem(StatusCodes.Status404NotFound);

        partner.MapGet("/establishments/{id:guid}/group-bookings", async (Guid id, ClaimsPrincipal user, GroupBookingService service, CancellationToken ct) =>
                Respond(await service.ForEstablishmentAsync(user.UserId(), id, ct)))
            .Produces<IReadOnlyList<GroupBookingDto>>().ProducesProblem(StatusCodes.Status404NotFound);

        partner.MapPost("/group-bookings/{id:guid}/answer", async (Guid id, GroupBookingAnswerInput input, ClaimsPrincipal user, GroupBookingService service, CancellationToken ct) =>
                Respond(await service.AnswerAsync(user.UserId(), id, input, ct)))
            .Produces<GroupBookingDto>().ProducesProblem(StatusCodes.Status400BadRequest).ProducesProblem(StatusCodes.Status404NotFound).ProducesProblem(StatusCodes.Status409Conflict);
    }

    private static Guid? OptionalUserId(ClaimsPrincipal user) => Guid.TryParse(user.FindFirstValue("sub"), out var id) ? id : null;

    private static IResult Respond<T>(Result<T> result) => result.IsSuccess ? Results.Ok(result.Value) : ProblemResults.From(result.Error);
}
