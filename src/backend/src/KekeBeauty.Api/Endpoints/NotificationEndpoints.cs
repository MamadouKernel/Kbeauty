using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using KekeBeauty.Api.Http;
using KekeBeauty.Infrastructure.Notifications;
using KekeBeauty.Infrastructure.Scheduling;
using Microsoft.Extensions.Options;

namespace KekeBeauty.Api.Endpoints;

/// <summary>The in-app notification centre (PRD NOT-01, §9 <c>/me/notifications</c>).</summary>
public static class NotificationEndpoints
{
    public static IEndpointRouteBuilder MapNotificationEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1/me/notifications").WithTags("Notifications").RequireAuthorization();

        group.MapGet("/", async (ClaimsPrincipal user, NotificationService service, CancellationToken ct) =>
                Results.Ok(await service.ListAsync(user.UserId(), ct)))
            .Produces<NotificationList>();

        group.MapPost("/{id:guid}/read", async (Guid id, ClaimsPrincipal user, NotificationService service, CancellationToken ct) =>
                await service.MarkReadAsync(user.UserId(), id, ct) ? Results.NoContent() : Results.NotFound())
            .Produces(StatusCodes.Status204NoContent)
            .ProducesProblem(StatusCodes.Status404NotFound);

        group.MapPost("/read-all", async (ClaimsPrincipal user, NotificationService service, CancellationToken ct) =>
            {
                await service.MarkAllReadAsync(user.UserId(), ct);
                return Results.NoContent();
            })
            .Produces(StatusCodes.Status204NoContent);

        MapPush(app.MapGroup("/api/v1/me/push").WithTags("Notifications").RequireAuthorization());

        app.MapGet("/api/v1/me/notification-preferences", async (ClaimsPrincipal user, ChannelService service, CancellationToken ct) =>
                Results.Ok(await service.PreferencesAsync(user.UserId(), ct)))
            .WithTags("Notifications").RequireAuthorization().Produces<NotificationPreferencesDto>();

        app.MapPut("/api/v1/me/notification-preferences", async (NotificationPreferencesDto input, ClaimsPrincipal user, ChannelService service, CancellationToken ct) =>
                Results.Ok(await service.SavePreferencesAsync(user.UserId(), input, ct)))
            .WithTags("Notifications").RequireAuthorization().Produces<NotificationPreferencesDto>();

        MapDeliveryReports(app);
        return app;
    }

    private static void MapPush(RouteGroupBuilder group)
    {
        group.MapGet("/config", async (ChannelService service, CancellationToken ct) => Results.Ok(await service.PushConfigAsync(ct)))
            .Produces<PushConfigDto>();

        group.MapPost("/subscribe", async (PushSubscribeInput input, ClaimsPrincipal user, ChannelService service, CancellationToken ct) =>
            {
                var result = await service.SubscribeAsync(user.UserId(), input, ct);
                return result.IsSuccess ? Results.NoContent() : ProblemResults.From(result.Error);
            })
            .Produces(StatusCodes.Status204NoContent).ProducesProblem(StatusCodes.Status400BadRequest);

        group.MapPost("/unsubscribe", async (PushSubscribeInput input, ClaimsPrincipal user, ChannelService service, CancellationToken ct) =>
            {
                await service.UnsubscribeAsync(user.UserId(), input.Endpoint, ct);
                return Results.NoContent();
            })
            .Produces(StatusCodes.Status204NoContent);
    }

    /// <summary>
    /// LeTexto posts the delivery state of each SMS here (GET or POST, fields <c>id</c> and <c>statuts</c>). The reports are not signed,
    /// so the URL given to LeTexto carries a shared secret (<c>sig</c>); the secret only lets someone move the status of a known message.
    /// </summary>
    private static void MapDeliveryReports(IEndpointRouteBuilder app)
    {
        MapZavuWebhook(app);
        MapLeTextoReports(app);
    }

    /// <summary>
    /// Zavu posts WhatsApp delivery reports, template reviews and replies here. Anonymous but signed: the raw body is read (bounded) and
    /// the <c>X-Zavu-Signature</c> checked before anything is parsed. Refusals answer 401 with no detail; Zavu retries on any non-2xx.
    /// </summary>
    private static void MapZavuWebhook(IEndpointRouteBuilder app) =>
        app.MapPost("/api/v1/webhooks/whatsapp/zavu", async (HttpRequest request, ZavuWebhookHandler handler, CancellationToken ct) =>
            {
                if (request.ContentLength is > ZavuWebhookHandler.MaxBodyBytes)
                {
                    return Results.StatusCode(StatusCodes.Status413PayloadTooLarge);
                }

                using var buffer = new MemoryStream();
                var chunk = new byte[8192];
                int read;
                while ((read = await request.Body.ReadAsync(chunk, ct)) > 0)
                {
                    if (buffer.Length + read > ZavuWebhookHandler.MaxBodyBytes)
                    {
                        return Results.StatusCode(StatusCodes.Status413PayloadTooLarge);
                    }

                    buffer.Write(chunk, 0, read);
                }

                return await handler.HandleAsync(buffer.ToArray(), request.Headers[ZavuSignature.HeaderName].ToString(), ct) switch
                {
                    ZavuWebhookResult.Rejected => Results.Unauthorized(),
                    ZavuWebhookResult.Malformed => Results.BadRequest(),
                    _ => Results.Ok(),
                };
            })
            .WithTags("Notifications").ExcludeFromDescription().DisableAntiforgery().AllowAnonymous();

    private static void MapLeTextoReports(IEndpointRouteBuilder app) =>
        app.MapMethods("/api/v1/webhooks/sms/letexto", ["GET", "POST"], async (HttpRequest request, IOptions<LeTextoOptions> options, ChannelService service, CancellationToken ct) =>
            {
                var secret = options.Value.WebhookSecret;
                var supplied = request.Query["sig"].ToString();
                if (secret.Length == 0 || !CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(supplied), Encoding.UTF8.GetBytes(secret)))
                {
                    return Results.Unauthorized();
                }

                string? id = request.Query["id"];
                string? status = request.Query["statuts"].ToString() is { Length: > 0 } q ? q : request.Query["status"].ToString();
                if (request.HasJsonContentType())
                {
                    try
                    {
                        using var document = await JsonDocument.ParseAsync(request.Body, cancellationToken: ct);
                        var root = document.RootElement;
                        id ??= root.TryGetProperty("id", out var i) ? i.ToString() : null;
                        if (string.IsNullOrEmpty(status))
                        {
                            status = root.TryGetProperty("statuts", out var s) ? s.GetString() : root.TryGetProperty("status", out var s2) ? s2.GetString() : null;
                        }
                    }
                    catch (JsonException)
                    {
                        return Results.BadRequest();
                    }
                }

                await service.ApplyDeliveryReportAsync(id, status, ct);
                return Results.Ok();
            })
            .WithTags("Notifications").ExcludeFromDescription().DisableAntiforgery().AllowAnonymous();
}
