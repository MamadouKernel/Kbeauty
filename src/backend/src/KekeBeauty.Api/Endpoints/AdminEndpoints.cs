using System.Security.Claims;
using KekeBeauty.Api.Http;
using KekeBeauty.Infrastructure.Admin;
using KekeBeauty.Infrastructure.Backups;
using KekeBeauty.Modules.Administration.Domain;
using KekeBeauty.Modules.Identity.Domain;
using KekeBeauty.SharedKernel;
using Microsoft.AspNetCore.Mvc;

namespace KekeBeauty.Api.Endpoints;

/// <summary>Back-office (<c>/api/v1/admin</c>): accounts, moderation, finances, settings, audit trail (PRD ADM-01 to ADM-07).</summary>
public static class AdminEndpoints
{
    public static IEndpointRouteBuilder MapAdminEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1/admin").WithTags("Admin").RequireAuthorization(AdminReviewEndpoints.AdminPolicy);

        MapUsers(group);
        MapEstablishments(group);
        MapFinance(group);
        MapSettings(group);
        MapLegal(group);
        MapChannels(group);
        MapPayouts(group);
        MapAudit(group);
        MapAdmins(app.MapGroup("/api/v1/admin/admins").WithTags("Admin").RequireAuthorization("SuperAdmin"));
        MapBackups(app.MapGroup("/api/v1/admin/backups").WithTags("Admin").RequireAuthorization("SuperAdmin"));
        return app;
    }

    private static AdminContext Context(ClaimsPrincipal user, HttpContext http) =>
        new(user.UserId(), user.IsInRole(AppRoles.SuperAdmin), http.Connection.RemoteIpAddress?.ToString());

    private static IResult Respond<T>(Result<T> result) => result.IsSuccess ? Results.Ok(result.Value) : ProblemResults.From(result.Error);

    private static IResult NoContent(Result result) => result.IsSuccess ? Results.NoContent() : ProblemResults.From(result.Error);

    private static IResult Created(Result<Guid> result) =>
        result.IsSuccess ? Results.Created((string?)null, new CreatedId(result.Value)) : ProblemResults.From(result.Error);

    private static void MapUsers(RouteGroupBuilder group)
    {
        group.MapGet("/users", async ([FromQuery] string? q, [FromQuery] string? role, [FromQuery] string? status, [FromQuery] int? page, [FromQuery] int? pageSize, AdminUserService service, CancellationToken ct) =>
                Results.Ok(await service.ListAsync(new AdminUserFilter(q, role, status), page, pageSize, ct)))
            .Produces<PageOf<AdminUserDto>>();

        group.MapGet("/users/{id:guid}", async (Guid id, AdminUserService service, CancellationToken ct) =>
                Respond(await service.GetAsync(id, ct)))
            .Produces<AdminUserDetail>().ProducesProblem(StatusCodes.Status404NotFound);

        group.MapPost("/users/{id:guid}/suspend", async (Guid id, ReasonInput input, ClaimsPrincipal user, HttpContext http, AdminUserService service, CancellationToken ct) =>
                NoContent(await service.SuspendAsync(Context(user, http), id, input.Reason, ct)))
            .Produces(StatusCodes.Status204NoContent).ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status403Forbidden).ProducesProblem(StatusCodes.Status409Conflict);

        group.MapPost("/users/{id:guid}/reactivate", async (Guid id, ReasonInput input, ClaimsPrincipal user, HttpContext http, AdminUserService service, CancellationToken ct) =>
                NoContent(await service.ReactivateAsync(Context(user, http), id, input.Reason, ct)))
            .Produces(StatusCodes.Status204NoContent).ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status403Forbidden).ProducesProblem(StatusCodes.Status409Conflict);
    }

    private static void MapEstablishments(RouteGroupBuilder group)
    {
        group.MapGet("/establishments", async ([FromQuery] string? q, [FromQuery] string? status, [FromQuery] int? page, [FromQuery] int? pageSize, AdminModerationService service, CancellationToken ct) =>
                Results.Ok(await service.ListAsync(new AdminEstablishmentFilter(q, status), page, pageSize, ct)))
            .Produces<PageOf<AdminEstablishmentDto>>();

        group.MapGet("/establishments/{id:guid}", async (Guid id, AdminModerationService service, CancellationToken ct) =>
                Respond(await service.GetAsync(id, ct)))
            .Produces<AdminEstablishmentDetail>().ProducesProblem(StatusCodes.Status404NotFound);

        group.MapPost("/establishments/{id:guid}/suspend", async (Guid id, ReasonInput input, ClaimsPrincipal user, HttpContext http, AdminModerationService service, CancellationToken ct) =>
                NoContent(await service.SuspendAsync(Context(user, http), id, input.Reason, ct)))
            .Produces(StatusCodes.Status204NoContent).ProducesProblem(StatusCodes.Status400BadRequest).ProducesProblem(StatusCodes.Status409Conflict);

        group.MapPost("/establishments/{id:guid}/reactivate", async (Guid id, ReasonInput input, ClaimsPrincipal user, HttpContext http, AdminModerationService service, CancellationToken ct) =>
                NoContent(await service.ReactivateAsync(Context(user, http), id, input.Reason, ct)))
            .Produces(StatusCodes.Status204NoContent).ProducesProblem(StatusCodes.Status400BadRequest).ProducesProblem(StatusCodes.Status409Conflict);

        group.MapPost("/establishments/{id:guid}/media/{mediaId:guid}/remove", async (Guid id, Guid mediaId, ReasonInput input, ClaimsPrincipal user, HttpContext http, AdminModerationService service, CancellationToken ct) =>
                NoContent(await service.RemoveMediaAsync(Context(user, http), id, mediaId, input.Reason, ct)))
            .Produces(StatusCodes.Status204NoContent).ProducesProblem(StatusCodes.Status400BadRequest).ProducesProblem(StatusCodes.Status404NotFound);
    }

    private static void MapFinance(RouteGroupBuilder group)
    {
        group.MapGet("/finance/summary", async ([FromQuery] DateTimeOffset? from, [FromQuery] DateTimeOffset? to, AdminFinanceService service, CancellationToken ct) =>
                Respond(await service.SummaryAsync(from, to, ct)))
            .Produces<FinanceSummary>().ProducesProblem(StatusCodes.Status400BadRequest);

        group.MapPost("/finance/past-due/{subscriptionId:guid}/remind", async (Guid subscriptionId, ClaimsPrincipal user, HttpContext http, AdminFinanceService service, CancellationToken ct) =>
                NoContent(await service.RemindAsync(Context(user, http), subscriptionId, ct)))
            .Produces(StatusCodes.Status204NoContent).ProducesProblem(StatusCodes.Status404NotFound).ProducesProblem(StatusCodes.Status409Conflict);

        group.MapGet("/finance/payments.csv", async ([FromQuery] DateTimeOffset? from, [FromQuery] DateTimeOffset? to, AdminFinanceService service, CancellationToken ct) =>
            {
                var result = await service.PaymentsCsvAsync(from, to, ct);
                return result.IsSuccess
                    ? Results.File(System.Text.Encoding.UTF8.GetPreamble().Concat(System.Text.Encoding.UTF8.GetBytes(result.Value)).ToArray(), "text/csv; charset=utf-8", "paiements.csv")
                    : ProblemResults.From(result.Error);
            })
            .Produces(StatusCodes.Status200OK, contentType: "text/csv").ProducesProblem(StatusCodes.Status400BadRequest);
    }

    private static void MapPayouts(RouteGroupBuilder group)
    {
        group.MapGet("/payouts", async (KekeBeauty.Infrastructure.Billing.PayoutService service, CancellationToken ct) => Results.Ok(await service.AllPayoutsAsync(ct)))
            .Produces<IReadOnlyList<KekeBeauty.Infrastructure.Billing.PayoutDto>>();

        group.MapPost("/payouts/run", async (ClaimsPrincipal user, HttpContext http, KekeBeauty.Infrastructure.Billing.PayoutService service, CancellationToken ct) =>
                Results.Ok(await service.RunAllAsync(Context(user, http), ct)))
            .Produces<KekeBeauty.Infrastructure.Billing.PayoutRunReport>();

        group.MapPost("/payouts/establishments/{id:guid}", async (Guid id, ClaimsPrincipal user, HttpContext http, KekeBeauty.Infrastructure.Billing.PayoutService service, CancellationToken ct) =>
                Respond(await service.PayAsync(Context(user, http), id, ct)))
            .Produces<KekeBeauty.Infrastructure.Billing.PayoutDto>().ProducesProblem(StatusCodes.Status400BadRequest).ProducesProblem(StatusCodes.Status404NotFound).ProducesProblem(StatusCodes.Status502BadGateway);

        group.MapGet("/collaborator-payouts", async (KekeBeauty.Infrastructure.Billing.CollaboratorPayoutService service, CancellationToken ct) => Results.Ok(await service.AllAsync(ct)))
            .Produces<IReadOnlyList<KekeBeauty.Infrastructure.Billing.CollaboratorPayoutDto>>();

        group.MapGet("/refunds", async (KekeBeauty.Infrastructure.Billing.PayoutService service, CancellationToken ct) => Results.Ok(await service.RefundsAsync(ct)))
            .Produces<IReadOnlyList<KekeBeauty.Infrastructure.Billing.RefundDto>>();

        group.MapPost("/refunds/{id:guid}/retry", async (Guid id, ClaimsPrincipal user, HttpContext http, KekeBeauty.Infrastructure.Billing.AppointmentPaymentService service, CancellationToken ct) =>
                NoContent(await service.RetryRefundAsync(Context(user, http), id, ct)))
            .Produces(StatusCodes.Status204NoContent).ProducesProblem(StatusCodes.Status400BadRequest);

        group.MapPost("/refunds/{id:guid}/manual", async (Guid id, KekeBeauty.Infrastructure.Billing.RefundReferenceInput input, ClaimsPrincipal user, HttpContext http, KekeBeauty.Infrastructure.Billing.AppointmentPaymentService service, CancellationToken ct) =>
                NoContent(await service.MarkRefundedManuallyAsync(Context(user, http), id, input.Reference, ct)))
            .Produces(StatusCodes.Status204NoContent).ProducesProblem(StatusCodes.Status400BadRequest);
    }

    private static void MapChannels(RouteGroupBuilder group)
    {
        group.MapGet("/channels", async (KekeBeauty.Infrastructure.Notifications.ChannelService service, CancellationToken ct) => Results.Ok(await service.StatusAsync(ct)))
            .Produces<KekeBeauty.Infrastructure.Notifications.ChannelsStatus>();

        group.MapPost("/channels/sms/test", async (KekeBeauty.Infrastructure.Notifications.SmsTestInput input, KekeBeauty.Infrastructure.Notifications.ChannelService service, CancellationToken ct) =>
                NoContent(await service.SendTestSmsAsync(input.Phone, ct)))
            .Produces(StatusCodes.Status204NoContent).ProducesProblem(StatusCodes.Status400BadRequest);

        group.MapPost("/channels/letexto/senders", async (KekeBeauty.Infrastructure.Notifications.SenderInput input, KekeBeauty.Infrastructure.Notifications.ChannelService service, CancellationToken ct) =>
                Respond(await service.CreateSenderAsync(input.Name, ct)))
            .Produces<string>().ProducesProblem(StatusCodes.Status400BadRequest);
    }

    private static void MapLegal(RouteGroupBuilder group)
    {
        group.MapGet("/legal", async (LegalDocumentService service, CancellationToken ct) => Results.Ok(await service.ListAsync(ct)))
            .Produces<IReadOnlyList<LegalDocumentSummary>>();

        group.MapGet("/legal/{slug}", async (string slug, LegalDocumentService service, CancellationToken ct) =>
                Respond(await service.GetAsync(slug, ct)))
            .Produces<LegalDocumentAdminDto>().ProducesProblem(StatusCodes.Status404NotFound);

        group.MapGet("/legal/{slug}/versions", async (string slug, LegalDocumentService service, CancellationToken ct) =>
                Results.Ok(await service.VersionsAsync(slug, ct)))
            .Produces<IReadOnlyList<LegalVersionDto>>();

        group.MapPut("/legal/{slug}", async (string slug, LegalDocumentInput input, ClaimsPrincipal user, HttpContext http, LegalDocumentService service, CancellationToken ct) =>
                Respond(await service.UpdateAsync(Context(user, http), slug, input, ct)))
            .Produces<LegalDocumentAdminDto>().ProducesProblem(StatusCodes.Status400BadRequest).ProducesProblem(StatusCodes.Status404NotFound);
    }

    private static void MapSettings(RouteGroupBuilder group)
    {
        group.MapGet("/categories", async (AdminSettingsService service, CancellationToken ct) => Results.Ok(await service.CategoriesAsync(ct)))
            .Produces<IReadOnlyList<CategoryAdminDto>>();

        group.MapPost("/categories", async (CategoryInput input, ClaimsPrincipal user, HttpContext http, AdminSettingsService service, CancellationToken ct) =>
                Created(await service.CreateCategoryAsync(Context(user, http), input, ct)))
            .Produces<CreatedId>(StatusCodes.Status201Created).ProducesProblem(StatusCodes.Status400BadRequest).ProducesProblem(StatusCodes.Status409Conflict);

        group.MapPut("/categories/{id:guid}", async (Guid id, CategoryInput input, ClaimsPrincipal user, HttpContext http, AdminSettingsService service, CancellationToken ct) =>
                NoContent(await service.UpdateCategoryAsync(Context(user, http), id, input, ct)))
            .Produces(StatusCodes.Status204NoContent).ProducesProblem(StatusCodes.Status400BadRequest).ProducesProblem(StatusCodes.Status404NotFound);

        group.MapGet("/geo/{level}", async (string level, [FromQuery] Guid? parentId, AdminSettingsService service, CancellationToken ct) =>
                Respond(await service.GeoAsync(level, parentId, ct)))
            .Produces<IReadOnlyList<GeoAdminDto>>().ProducesProblem(StatusCodes.Status400BadRequest).ProducesProblem(StatusCodes.Status404NotFound);

        group.MapPost("/geo/{level}", async (string level, GeoCreateInput input, ClaimsPrincipal user, HttpContext http, AdminSettingsService service, CancellationToken ct) =>
                Created(await service.CreateGeoAsync(Context(user, http), level, input, ct)))
            .Produces<CreatedId>(StatusCodes.Status201Created).ProducesProblem(StatusCodes.Status400BadRequest).ProducesProblem(StatusCodes.Status409Conflict);

        group.MapPut("/geo/{level}/{id:guid}", async (string level, Guid id, GeoUpdateInput input, ClaimsPrincipal user, HttpContext http, AdminSettingsService service, CancellationToken ct) =>
                NoContent(await service.UpdateGeoAsync(Context(user, http), level, id, input, ct)))
            .Produces(StatusCodes.Status204NoContent).ProducesProblem(StatusCodes.Status400BadRequest).ProducesProblem(StatusCodes.Status404NotFound);

        group.MapGet("/settings", async (AppSettingsService service, CancellationToken ct) => Results.Ok(await service.GetAsync(ct)))
            .Produces<AppSettingsDto>();

        group.MapPut("/settings", async (AppSettingsDto input, ClaimsPrincipal user, HttpContext http, AppSettingsService service, CancellationToken ct) =>
                Respond(await service.UpdateAsync(Context(user, http), input, ct)))
            .Produces<AppSettingsDto>().ProducesProblem(StatusCodes.Status400BadRequest);

        group.MapGet("/templates", async (NotificationTemplateService service, CancellationToken ct) => Results.Ok(await service.ListAsync(ct)))
            .Produces<IReadOnlyList<TemplateDto>>();

        group.MapPut("/templates/{code}", async (string code, TemplateInput input, ClaimsPrincipal user, HttpContext http, NotificationTemplateService service, CancellationToken ct) =>
                NoContent(await service.UpdateAsync(Context(user, http), code, input, ct)))
            .Produces(StatusCodes.Status204NoContent).ProducesProblem(StatusCodes.Status400BadRequest).ProducesProblem(StatusCodes.Status404NotFound);

        group.MapDelete("/templates/{code}", async (string code, ClaimsPrincipal user, HttpContext http, NotificationTemplateService service, CancellationToken ct) =>
                NoContent(await service.ResetAsync(Context(user, http), code, ct)))
            .Produces(StatusCodes.Status204NoContent).ProducesProblem(StatusCodes.Status404NotFound);
    }

    private static void MapAudit(RouteGroupBuilder group) =>
        group.MapGet("/audit", async (
                [FromQuery] string? action,
                [FromQuery] string? entityType,
                [FromQuery] Guid? entityId,
                [FromQuery] Guid? actorId,
                [FromQuery] DateTimeOffset? from,
                [FromQuery] DateTimeOffset? to,
                [FromQuery] int? page,
                [FromQuery] int? pageSize,
                AuditQueryService service,
                CancellationToken ct) =>
                Results.Ok(await service.ListAsync(new AuditFilter(action, entityType, entityId, actorId, from, to), page, pageSize, ct)))
            .Produces<PageOf<AuditEntryDto>>();

    private static void MapBackups(RouteGroupBuilder group)
    {
        group.MapGet("/", async (BackupService service, CancellationToken ct) => Results.Ok(await service.ListAsync(ct)))
            .Produces<IReadOnlyList<BackupDto>>();

        // The dump runs inside the request (seconds for a database of this size); a failed dump comes back as a row with status Failed.
        group.MapPost("/", async (ClaimsPrincipal user, HttpContext http, BackupService service, CancellationToken ct) =>
            {
                var result = await service.CreateAsync(Context(user, http), BackupKind.Manual, ct);
                return result.IsSuccess ? Results.Created($"/api/v1/admin/backups/{result.Value.Id}", result.Value) : ProblemResults.From(result.Error);
            })
            .Produces<BackupDto>(StatusCodes.Status201Created).ProducesProblem(StatusCodes.Status409Conflict);

        group.MapGet("/{id:guid}/download", async (Guid id, ClaimsPrincipal user, HttpContext http, BackupService service, CancellationToken ct) =>
            {
                var result = await service.OpenAsync(Context(user, http), id, ct);
                return result.IsSuccess
                    ? Results.File(result.Value.File.Content, result.Value.File.ContentType, result.Value.FileName)
                    : ProblemResults.From(result.Error);
            })
            .Produces(StatusCodes.Status200OK, contentType: "application/octet-stream")
            .ProducesProblem(StatusCodes.Status404NotFound).ProducesProblem(StatusCodes.Status409Conflict);

        group.MapDelete("/{id:guid}", async (Guid id, ClaimsPrincipal user, HttpContext http, BackupService service, CancellationToken ct) =>
                NoContent(await service.DeleteAsync(Context(user, http), id, ct)))
            .Produces(StatusCodes.Status204NoContent).ProducesProblem(StatusCodes.Status404NotFound).ProducesProblem(StatusCodes.Status409Conflict);
    }

    private static void MapAdmins(RouteGroupBuilder group)
    {
        group.MapGet("/", async (AdminUserService service, CancellationToken ct) => Results.Ok(await service.ListAdminsAsync(ct)))
            .Produces<IReadOnlyList<AdminUserDto>>();

        group.MapPost("/", async (GrantAdminInput input, ClaimsPrincipal user, HttpContext http, AdminUserService service, CancellationToken ct) =>
                Respond(await service.GrantAdminAsync(Context(user, http), input.Email, ct)))
            .Produces<AdminUserDto>().ProducesProblem(StatusCodes.Status400BadRequest).ProducesProblem(StatusCodes.Status404NotFound).ProducesProblem(StatusCodes.Status409Conflict);

        group.MapDelete("/{id:guid}", async (Guid id, ClaimsPrincipal user, HttpContext http, AdminUserService service, CancellationToken ct) =>
                NoContent(await service.RevokeAdminAsync(Context(user, http), id, ct)))
            .Produces(StatusCodes.Status204NoContent).ProducesProblem(StatusCodes.Status403Forbidden).ProducesProblem(StatusCodes.Status409Conflict);
    }
}
