using System.Security.Claims;
using System.Threading.RateLimiting;
using KekeBeauty.Api.Http;
using KekeBeauty.Infrastructure.Billing;
using KekeBeauty.SharedKernel;
using Microsoft.Extensions.Options;

namespace KekeBeauty.Api.Endpoints;

/// <summary>Plans, subscriptions, invoices and payment notifications (PRD BIL-01 to BIL-08).</summary>
public static class BillingEndpoints
{
    /// <summary>
    /// Each call to the payment refresh makes the server query the payment provider: bounded per signed-in user so that nobody can use
    /// the API to hammer the provider. The subscription page polls at most 30 times a minute.
    /// </summary>
    public const string PaymentRefreshRateLimitPolicy = "payment-refresh";

    public static IServiceCollection AddBillingRateLimits(this IServiceCollection services) =>
        services.AddRateLimiter(limiter => limiter.AddPolicy(PaymentRefreshRateLimitPolicy, context =>
            SharedRateLimit.FixedWindow(
                context.RequestServices,
                "payment-refresh",
                context.User.FindFirstValue("sub") ?? context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
                context.RequestServices.GetRequiredService<IConfiguration>().GetValue("RateLimits:PaymentRefreshPerMinute", 40),
                TimeSpan.FromMinutes(1))));

    public static IEndpointRouteBuilder MapBillingEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/v1/plans", async (BillingService service, CancellationToken ct) => Results.Ok(await service.PlansAsync(false, ct)))
            .WithTags("Billing").AllowAnonymous().Produces<IReadOnlyList<PlanDto>>();

        MapPartner(app.MapGroup("/api/v1/pro").WithTags("Billing").RequireAuthorization(PartnerEndpoints.PartnerPolicy));
        MapAdmin(app.MapGroup("/api/v1/admin/plans").WithTags("Admin").RequireAuthorization(AdminReviewEndpoints.AdminFinancePolicy));
        MapPromoAdmin(app.MapGroup("/api/v1/admin/promo-codes").WithTags("Admin").RequireAuthorization(AdminReviewEndpoints.AdminFinancePolicy));
        MapWebhooks(app);
        return app;
    }

    private static IResult Respond<T>(Result<T> result) => result.IsSuccess ? Results.Ok(result.Value) : ProblemResults.From(result.Error);

    private static void MapPartner(RouteGroupBuilder group)
    {
        group.MapGet("/establishments/{id:guid}/subscription", async (Guid id, ClaimsPrincipal user, BillingService service, CancellationToken ct) =>
                Respond(await service.OverviewAsync(user.UserId(), id, ct)))
            .Produces<SubscriptionOverview>().ProducesProblem(StatusCodes.Status404NotFound);

        // The customer is back from the payment page: re-read the provider instead of waiting for a notification that may not come.
        group.MapPost("/establishments/{id:guid}/subscription/refresh", async (Guid id, ClaimsPrincipal user, PaymentProcessor processor, CancellationToken ct) =>
                Respond(await processor.RefreshPendingAsync(user.UserId(), id, ct)))
            .RequireRateLimiting(PaymentRefreshRateLimitPolicy)
            .Produces<int>().ProducesProblem(StatusCodes.Status404NotFound);

        group.MapPost("/establishments/{id:guid}/subscription", async (Guid id, SubscribeInput input, ClaimsPrincipal user, BillingService service, CancellationToken ct) =>
                Respond(await service.SubscribeAsync(user.UserId(), id, input, ct)))
            .Produces<CheckoutDto>().ProducesProblem(StatusCodes.Status400BadRequest).ProducesProblem(StatusCodes.Status409Conflict)
            .ProducesProblem(StatusCodes.Status502BadGateway);

        group.MapPost("/promo-codes/check", async (SubscribeInput input, PromoService service, CancellationToken ct) =>
                Respond(await service.CheckAsync(input, ct)))
            .RequireRateLimiting(GrowthEndpoints.GiftCardRateLimitPolicy)
            .Produces<PromoCheckDto>().ProducesProblem(StatusCodes.Status400BadRequest);

        group.MapPost("/establishments/{id:guid}/subscription/trial", async (Guid id, SubscribeInput input, ClaimsPrincipal user, BillingService service, CancellationToken ct) =>
                Respond(await service.StartTrialAsync(user.UserId(), id, input, ct)))
            .Produces<SubscriptionDto>().ProducesProblem(StatusCodes.Status400BadRequest).ProducesProblem(StatusCodes.Status409Conflict);

        group.MapPost("/subscriptions/{id:guid}/trial/pay", async (Guid id, ClaimsPrincipal user, BillingService service, CancellationToken ct) =>
                Respond(await service.PayTrialAsync(user.UserId(), id, ct)))
            .Produces<CheckoutDto>().ProducesProblem(StatusCodes.Status404NotFound).ProducesProblem(StatusCodes.Status409Conflict)
            .ProducesProblem(StatusCodes.Status502BadGateway);

        group.MapPost("/subscriptions/{id:guid}/change-quote", async (Guid id, SubscribeInput input, ClaimsPrincipal user, BillingService service, CancellationToken ct) =>
                Respond(await service.QuoteChangeAsync(user.UserId(), id, input, ct)))
            .Produces<ChangeQuoteDto>().ProducesProblem(StatusCodes.Status404NotFound).ProducesProblem(StatusCodes.Status409Conflict);

        group.MapPost("/subscriptions/{id:guid}/change", async (Guid id, SubscribeInput input, ClaimsPrincipal user, BillingService service, CancellationToken ct) =>
                Respond(await service.ChangePlanAsync(user.UserId(), id, input, ct)))
            .Produces<ChangeResultDto>().ProducesProblem(StatusCodes.Status404NotFound).ProducesProblem(StatusCodes.Status409Conflict)
            .ProducesProblem(StatusCodes.Status502BadGateway);

        group.MapDelete("/subscriptions/{id:guid}/change", async (Guid id, ClaimsPrincipal user, BillingService service, CancellationToken ct) =>
                PartnerEndpoints.NoContentOr(await service.CancelScheduledChangeAsync(user.UserId(), id, ct)))
            .Produces(StatusCodes.Status204NoContent).ProducesProblem(StatusCodes.Status404NotFound).ProducesProblem(StatusCodes.Status409Conflict);

        group.MapDelete("/subscriptions/{id:guid}", async (Guid id, ClaimsPrincipal user, BillingService service, CancellationToken ct) =>
                PartnerEndpoints.NoContentOr(await service.CancelAsync(user.UserId(), id, ct)))
            .Produces(StatusCodes.Status204NoContent).ProducesProblem(StatusCodes.Status404NotFound).ProducesProblem(StatusCodes.Status409Conflict);

        group.MapGet("/invoices", async (ClaimsPrincipal user, BillingService service, CancellationToken ct) =>
                Results.Ok(await service.InvoicesAsync(user.UserId(), ct)))
            .Produces<IReadOnlyList<InvoiceDto>>();

        group.MapPost("/invoices/{id:guid}/pay", async (Guid id, ClaimsPrincipal user, BillingService service, CancellationToken ct) =>
                Respond(await service.PayInvoiceAsync(user.UserId(), id, ct)))
            .Produces<CheckoutDto>().ProducesProblem(StatusCodes.Status404NotFound).ProducesProblem(StatusCodes.Status409Conflict)
            .ProducesProblem(StatusCodes.Status502BadGateway);

        group.MapGet("/invoices/{id:guid}/pdf", async (Guid id, ClaimsPrincipal user, BillingService service, CancellationToken ct) =>
            {
                var result = await service.InvoicePdfAsync(user.UserId(), id, ct);
                return result.IsSuccess
                    ? Results.File(result.Value.Content, "application/pdf", result.Value.FileName)
                    : ProblemResults.From(result.Error);
            })
            .Produces(StatusCodes.Status200OK, contentType: "application/pdf").ProducesProblem(StatusCodes.Status404NotFound);

        group.MapGet("/establishments/{id:guid}/finances", async (Guid id, DateTimeOffset? from, DateTimeOffset? to, ClaimsPrincipal user, PartnerFinanceService service, CancellationToken ct) =>
                Respond(await service.SummaryAsync(user.UserId(), id, from, to, ct)))
            .Produces<PartnerFinanceSummary>().ProducesProblem(StatusCodes.Status400BadRequest).ProducesProblem(StatusCodes.Status404NotFound);

        group.MapGet("/establishments/{id:guid}/finances.csv", async (Guid id, DateTimeOffset? from, DateTimeOffset? to, ClaimsPrincipal user, PartnerFinanceService service, CancellationToken ct) =>
            {
                var result = await service.ExportCsvAsync(user.UserId(), id, from, to, ct);
                return result.IsSuccess
                    ? Results.File(result.Value.Content, "text/csv; charset=utf-8", result.Value.FileName)
                    : ProblemResults.From(result.Error);
            })
            .Produces(StatusCodes.Status200OK, contentType: "text/csv").ProducesProblem(StatusCodes.Status400BadRequest).ProducesProblem(StatusCodes.Status404NotFound);

        group.MapGet("/appointments/{id:guid}/receipt.pdf", async (Guid id, ClaimsPrincipal user, PartnerFinanceService service, CancellationToken ct) =>
            {
                var result = await service.AppointmentReceiptPdfAsync(user.UserId(), id, ct);
                return result.IsSuccess
                    ? Results.File(result.Value.Content, "application/pdf", result.Value.FileName)
                    : ProblemResults.From(result.Error);
            })
            .Produces(StatusCodes.Status200OK, contentType: "application/pdf").ProducesProblem(StatusCodes.Status404NotFound);

        group.MapGet("/payouts/{id:guid}/receipt.pdf", async (Guid id, ClaimsPrincipal user, PartnerFinanceService service, CancellationToken ct) =>
            {
                var result = await service.PayoutReceiptPdfAsync(user.UserId(), id, ct);
                return result.IsSuccess
                    ? Results.File(result.Value.Content, "application/pdf", result.Value.FileName)
                    : ProblemResults.From(result.Error);
            })
            .Produces(StatusCodes.Status200OK, contentType: "application/pdf").ProducesProblem(StatusCodes.Status404NotFound);
    }

    private static void MapAdmin(RouteGroupBuilder group)
    {
        group.MapGet("/", async (BillingService service, CancellationToken ct) => Results.Ok(await service.PlansAsync(true, ct)))
            .Produces<IReadOnlyList<PlanDto>>();

        group.MapPost("/", async (PlanUpdate input, ClaimsPrincipal user, BillingService service, CancellationToken ct) =>
            {
                var result = await service.CreatePlanAsync(user.UserId(), input, ct);
                return result.IsSuccess ? Results.Created($"/api/v1/admin/plans/{result.Value.Id}", result.Value) : ProblemResults.From(result.Error);
            })
            .Produces<PlanDto>(StatusCodes.Status201Created).ProducesProblem(StatusCodes.Status400BadRequest);

        group.MapPut("/{id:guid}", async (Guid id, PlanUpdate input, ClaimsPrincipal user, BillingService service, CancellationToken ct) =>
                Respond(await service.UpdatePlanAsync(user.UserId(), id, input, ct)))
            .Produces<PlanDto>().ProducesProblem(StatusCodes.Status400BadRequest).ProducesProblem(StatusCodes.Status404NotFound);

        group.MapDelete("/{id:guid}", async (Guid id, ClaimsPrincipal user, BillingService service, CancellationToken ct) =>
            {
                var result = await service.DeletePlanAsync(user.UserId(), id, ct);
                return result.IsSuccess ? Results.NoContent() : ProblemResults.From(result.Error);
            })
            .Produces(StatusCodes.Status204NoContent).ProducesProblem(StatusCodes.Status400BadRequest).ProducesProblem(StatusCodes.Status404NotFound).ProducesProblem(StatusCodes.Status409Conflict);
    }

    private static void MapPromoAdmin(RouteGroupBuilder group)
    {
        group.MapGet("/", async (PromoService service, CancellationToken ct) => Results.Ok(await service.ListAsync(ct)))
            .Produces<IReadOnlyList<PromoCodeDto>>();

        group.MapPost("/", async (PromoCodeInput input, ClaimsPrincipal user, PromoService service, CancellationToken ct) =>
            {
                var result = await service.CreateAsync(user.UserId(), input, ct);
                return result.IsSuccess ? Results.Created($"/api/v1/admin/promo-codes/{result.Value.Id}", result.Value) : ProblemResults.From(result.Error);
            })
            .Produces<PromoCodeDto>(StatusCodes.Status201Created).ProducesProblem(StatusCodes.Status400BadRequest).ProducesProblem(StatusCodes.Status409Conflict);

        group.MapPut("/{id:guid}", async (Guid id, PromoCodeInput input, ClaimsPrincipal user, PromoService service, CancellationToken ct) =>
                Respond(await service.UpdateAsync(user.UserId(), id, input, ct)))
            .Produces<PromoCodeDto>().ProducesProblem(StatusCodes.Status400BadRequest).ProducesProblem(StatusCodes.Status404NotFound);
    }

    private static void MapWebhooks(IEndpointRouteBuilder app)
    {
        // The provider calls this anonymously: authenticity comes from the signature, and the real status is then
        // re-read from the provider, so a forged call can at worst trigger a harmless verification.
        // Providers call this anonymously, by POST (form) or GET: authenticity comes from the gateway's check (a signature or a signed
        // callback URL), and the real outcome is then re-read from the provider, so a forged call can at worst trigger a verification.
        app.MapMethods("/api/v1/webhooks/payments/{provider}", ["GET", "POST"], async (string provider, HttpRequest request, PaymentProcessor processor, CancellationToken ct) =>
            {
                var gateway = processor.GatewayFor(provider);
                if (gateway is null)
                {
                    return Results.NotFound();
                }

                var fields = request.Query.ToDictionary(q => q.Key, q => q.Value.ToString());
                if (request.HasFormContentType)
                {
                    foreach (var field in await request.ReadFormAsync(ct))
                    {
                        fields[field.Key] = field.Value.ToString();
                    }
                }

                var token = request.Headers["x-token"].ToString();
                if (string.IsNullOrEmpty(token))
                {
                    token = fields.GetValueOrDefault("sig") ?? string.Empty;
                }

                if (!gateway.IsNotificationAuthentic(fields, token))
                {
                    Telemetry.PaymentNotifications.Add(1, new KeyValuePair<string, object?>("provider", provider), new KeyValuePair<string, object?>("result", "rejected_signature"));
                    return Results.Unauthorized();
                }

                if (gateway.TransactionIdFrom(fields) is not { Length: > 0 } transactionId)
                {
                    return Results.BadRequest();
                }

                var raw = string.Join('&', fields.Where(f => f.Key != "sig").Select(f => $"{f.Key}={f.Value}"));
                var outcome = await processor.HandleNotificationAsync(provider, transactionId, transactionId, raw, ct);
                Telemetry.PaymentNotifications.Add(1, new KeyValuePair<string, object?>("provider", provider),
                    new KeyValuePair<string, object?>("result", outcome.ToString().ToLowerInvariant()));
                // Deferred means "ask me again": a non-2xx makes the provider retry.
                return outcome == NotificationOutcome.Deferred ? Results.StatusCode(StatusCodes.Status503ServiceUnavailable) : Results.Ok();
            })
            .WithTags("Billing").ExcludeFromDescription().DisableAntiforgery().AllowAnonymous();

        // Development only: stands in for the customer on the payment provider's page.
        if (app is WebApplication web && web.Services.GetRequiredService<IOptions<PaymentsOptions>>().Value.IsFake)
        {
            app.MapPost("/api/v1/dev/payments/{transactionId}/simulate", async (string transactionId, SimulateInput input, FakePaymentGateway gateway, PaymentProcessor processor, CancellationToken ct) =>
                {
                    var status = string.Equals(input.Status, "accepted", StringComparison.OrdinalIgnoreCase) ? GatewayStatus.Accepted : GatewayStatus.Refused;
                    if (!gateway.Settle(transactionId, status))
                    {
                        return Results.NotFound();
                    }

                    await processor.HandleNotificationAsync(PaymentsOptions.FakeProvider, transactionId, transactionId, $"simulated:{status}", ct);
                    return Results.NoContent();
                })
                .WithTags("Billing").AllowAnonymous().Produces(StatusCodes.Status204NoContent).ProducesProblem(StatusCodes.Status404NotFound);
        }
    }
}
