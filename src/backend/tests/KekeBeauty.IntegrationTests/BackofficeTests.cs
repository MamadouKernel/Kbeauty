using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using KekeBeauty.Api.Endpoints;
using KekeBeauty.Infrastructure.Admin;
using KekeBeauty.Infrastructure.Billing;
using KekeBeauty.Infrastructure.Persistence;
using KekeBeauty.Infrastructure.Scheduling;
using KekeBeauty.Modules.Identity.Application;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace KekeBeauty.IntegrationTests;

[Collection(ApiCollection.Name)]
public sealed class BackofficeTests(ApiFixture api)
{
    private const string Admin = "/api/v1/admin";

    private async Task WithDbAsync(Func<KekeBeautyDbContext, Task> action)
    {
        await using var scope = api.Services.CreateAsyncScope();
        await action(scope.ServiceProvider.GetRequiredService<KekeBeautyDbContext>());
    }

    private static Task<HttpResponseMessage> PostReasonAsync(UserSession session, string url, string? reason) =>
        session.Client.PostAsJsonAsync(url, new { reason });

    private static async Task<AdminUserDto> UserAsync(UserSession admin, Guid id) =>
        (await admin.Client.GetFromJsonAsync<AdminUserDetail>($"{Admin}/users/{id}"))!.User;

    // ───────── Access ─────────

    [Fact]
    public async Task Only_administrators_may_use_the_back_office()
    {
        using var client = await TestAccounts.ClientAsync(api);
        using var partner = await TestAccounts.PartnerAsync(api);
        using var admin = await TestAccounts.AdminAsync(api);
        using var anonymous = api.CreateClient();

        (await anonymous.GetAsync($"{Admin}/users")).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        foreach (var path in new[] { "users", "establishments", "finance/summary", "categories", "settings", "templates", "audit" })
        {
            (await client.Client.GetAsync($"{Admin}/{path}")).StatusCode.Should().Be(HttpStatusCode.Forbidden, path);
            (await partner.Client.GetAsync($"{Admin}/{path}")).StatusCode.Should().Be(HttpStatusCode.Forbidden, path);
            (await admin.Client.GetAsync($"{Admin}/{path}")).StatusCode.Should().Be(HttpStatusCode.OK, path);
        }

        // The administrator list is for SuperAdmins only.
        (await admin.Client.GetAsync($"{Admin}/admins")).StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    // ───────── Users (ADM-01) and audit (ADM-06) ─────────

    [Fact]
    public async Task Accounts_can_be_searched_suspended_and_reactivated_with_a_reason_and_an_audit_trail()
    {
        using var admin = await TestAccounts.AdminAsync(api);
        using var other = await TestAccounts.AdminAsync(api);
        using var client = await TestAccounts.ClientAsync(api);

        var found = (await admin.Client.GetFromJsonAsync<PageOf<AdminUserDto>>($"{Admin}/users?q={Uri.EscapeDataString(client.Email)}"))!;
        found.Total.Should().Be(1);
        found.Items.Single().Roles.Should().Equal("Client");

        (await PostReasonAsync(admin, $"{Admin}/users/{client.UserId}/suspend", null)).StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await PostReasonAsync(admin, $"{Admin}/users/{client.UserId}/suspend", "Fausses réservations répétées")).StatusCode.Should().Be(HttpStatusCode.NoContent);
        (await UserAsync(admin, client.UserId)).Status.Should().Be("Suspended");
        (await PostReasonAsync(admin, $"{Admin}/users/{client.UserId}/suspend", "Encore")).StatusCode.Should().Be(HttpStatusCode.Conflict);

        using var anonymous = api.CreateClient();
        (await anonymous.PostAsJsonAsync("/api/v1/auth/login", new LoginRequest(client.Email, TestAccounts.Password))).IsSuccessStatusCode.Should().BeFalse();

        (await PostReasonAsync(admin, $"{Admin}/users/{client.UserId}/reactivate", "Erreur de modération")).StatusCode.Should().Be(HttpStatusCode.NoContent);
        (await UserAsync(admin, client.UserId)).Status.Should().Be("Active");

        // Not oneself, and an Admin cannot touch another Admin.
        (await PostReasonAsync(admin, $"{Admin}/users/{admin.UserId}/suspend", "Test")).StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await PostReasonAsync(admin, $"{Admin}/users/{other.UserId}/suspend", "Test")).StatusCode.Should().Be(HttpStatusCode.Forbidden);

        var trail = (await admin.Client.GetFromJsonAsync<PageOf<AuditEntryDto>>($"{Admin}/audit?entityId={client.UserId}"))!;
        trail.Items.Select(i => i.Action).Should().Contain(["user.suspend", "user.reactivate"]);
        trail.Items.Should().OnlyContain(i => i.ActorEmail == admin.Email && i.EntityType == "User");
        trail.Items.First(i => i.Action == "user.suspend").Data.Should().Contain("Fausses réservations");
    }

    // ───────── Administrators (ADM-07) ─────────

    [Fact]
    public async Task A_super_admin_grants_and_revokes_the_admin_role()
    {
        using var superAdmin = await TestAccounts.SuperAdminAsync(api);
        using var otherSuper = await TestAccounts.SuperAdminAsync(api);
        using var admin = await TestAccounts.AdminAsync(api);
        using var partner = await TestAccounts.PartnerAsync(api);

        (await admin.Client.PostAsJsonAsync($"{Admin}/admins", new { email = partner.Email })).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await superAdmin.Client.PostAsJsonAsync($"{Admin}/admins", new { email = "nobody@example.com" })).StatusCode.Should().Be(HttpStatusCode.NotFound);

        var granted = await superAdmin.Client.PostAsJsonAsync($"{Admin}/admins", new { email = partner.Email });
        granted.StatusCode.Should().Be(HttpStatusCode.OK);
        (await granted.Content.ReadFromJsonAsync<AdminUserDto>())!.Roles.Should().Contain("Admin");
        (await superAdmin.Client.PostAsJsonAsync($"{Admin}/admins", new { email = partner.Email })).StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await superAdmin.Client.GetFromJsonAsync<List<AdminUserDto>>($"{Admin}/admins"))!.Select(a => a.Id).Should().Contain(partner.UserId);

        (await superAdmin.Client.DeleteAsync($"{Admin}/admins/{superAdmin.UserId}")).StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await superAdmin.Client.DeleteAsync($"{Admin}/admins/{otherSuper.UserId}")).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await superAdmin.Client.DeleteAsync($"{Admin}/admins/{partner.UserId}")).StatusCode.Should().Be(HttpStatusCode.NoContent);
        (await UserAsync(superAdmin, partner.UserId)).Roles.Should().NotContain("Admin");
        (await superAdmin.Client.DeleteAsync($"{Admin}/admins/{partner.UserId}")).StatusCode.Should().Be(HttpStatusCode.Conflict);
    }

    // ───────── Moderation (ADM-02) ─────────

    [Fact]
    public async Task A_suspended_listing_disappears_and_its_owner_is_told_why()
    {
        using var admin = await TestAccounts.AdminAsync(api);
        var salon = await TestSalons.PublishAsync(api, admin, subscribe: false);
        try
        {
            using var anonymous = api.CreateClient();
            (await anonymous.GetAsync($"/api/v1/establishments/{salon.Slug}")).StatusCode.Should().Be(HttpStatusCode.OK);

            var url = $"{Admin}/establishments/{salon.Id}";
            (await PostReasonAsync(admin, $"{url}/suspend", " ")).StatusCode.Should().Be(HttpStatusCode.BadRequest);
            (await PostReasonAsync(admin, $"{url}/reactivate", "Rien à rétablir")).StatusCode.Should().Be(HttpStatusCode.Conflict);
            (await PostReasonAsync(admin, $"{url}/suspend", "Photos trompeuses signalées")).StatusCode.Should().Be(HttpStatusCode.NoContent);

            (await anonymous.GetAsync($"/api/v1/establishments/{salon.Slug}")).StatusCode.Should().Be(HttpStatusCode.NotFound);
            (await salon.Owner.Client.GetFromJsonAsync<NotificationList>("/api/v1/me/notifications"))!.Items
                .Should().Contain(n => n.TemplateCode == "moderation.suspended" && n.Body.Contains("Photos trompeuses"));

            var detail = (await admin.Client.GetFromJsonAsync<AdminEstablishmentDetail>(url))!;
            detail.Establishment.Status.Should().Be("Suspended");
            detail.Establishment.OwnerEmail.Should().Be(salon.Owner.Email);
            detail.History.Select(h => h.Action).Should().Contain("establishment.suspend");

            (await PostReasonAsync(admin, $"{url}/reactivate", "Photos corrigées")).StatusCode.Should().Be(HttpStatusCode.NoContent);
            (await anonymous.GetAsync($"/api/v1/establishments/{salon.Slug}")).StatusCode.Should().Be(HttpStatusCode.OK);

            var listed = (await admin.Client.GetFromJsonAsync<PageOf<AdminEstablishmentDto>>($"{Admin}/establishments?q={Uri.EscapeDataString(salon.Slug[..8])}&status=Active"))!;
            listed.Items.Should().Contain(e => e.Id == salon.Id);
        }
        finally
        {
            await TestCleanup.DeleteEstablishmentsAsync(api, salon.Owner.UserId);
        }
    }

    [Fact]
    public async Task A_non_compliant_photo_can_be_removed_with_a_reason()
    {
        using var admin = await TestAccounts.AdminAsync(api);
        var salon = await TestSalons.PublishAsync(api, admin, subscribe: false);
        try
        {
            var url = $"{Admin}/establishments/{salon.Id}";
            var media = (await admin.Client.GetFromJsonAsync<AdminEstablishmentDetail>(url))!.Media.Should().ContainSingle().Subject;

            (await PostReasonAsync(admin, $"{url}/media/{Guid.NewGuid()}/remove", "Image inappropriée")).StatusCode.Should().Be(HttpStatusCode.NotFound);
            (await PostReasonAsync(admin, $"{url}/media/{media.Id}/remove", null)).StatusCode.Should().Be(HttpStatusCode.BadRequest);
            (await PostReasonAsync(admin, $"{url}/media/{media.Id}/remove", "Image inappropriée")).StatusCode.Should().Be(HttpStatusCode.NoContent);

            var detail = (await admin.Client.GetFromJsonAsync<AdminEstablishmentDetail>(url))!;
            detail.Media.Should().BeEmpty();
            detail.History.Select(h => h.Action).Should().Contain("establishment.media.remove");
            (await salon.Owner.Client.GetFromJsonAsync<NotificationList>("/api/v1/me/notifications"))!.Items
                .Should().Contain(n => n.TemplateCode == "moderation.media_removed");
        }
        finally
        {
            await TestCleanup.DeleteEstablishmentsAsync(api, salon.Owner.UserId);
        }
    }

    // ───────── Finance (ADM-04) ─────────

    [Fact]
    public async Task The_dashboard_counts_plans_revenue_and_exports_payments_as_csv()
    {
        using var admin = await TestAccounts.AdminAsync(api);
        var salon = await TestSalons.PublishAsync(api, admin);
        try
        {
            var summary = (await admin.Client.GetFromJsonAsync<FinanceSummary>($"{Admin}/finance/summary"))!;
            summary.Plans.Single(p => p.PlanCode == "premium").Active.Should().BeGreaterThanOrEqualTo(1);
            summary.MonthlyRecurringRevenue.Should().BeGreaterThanOrEqualTo(15000);
            summary.Revenue.Should().BeGreaterThanOrEqualTo(15000);
            summary.RevenueByMethod.Should().Contain(m => m.Method == "Wave" && m.Amount >= 15000);

            var invoice = (await salon.Owner.Client.GetFromJsonAsync<List<InvoiceDto>>("/api/v1/pro/invoices"))!.Single();
            var csv = await admin.Client.GetAsync($"{Admin}/finance/payments.csv");
            csv.StatusCode.Should().Be(HttpStatusCode.OK);
            csv.Content.Headers.ContentType!.MediaType.Should().Be("text/csv");
            var text = await csv.Content.ReadAsStringAsync();
            text.TrimStart('﻿').Should().StartWith("date;facture;etablissement;montant;devise;moyen;passerelle;transaction");
            text.Should().Contain(invoice.Number).And.Contain(";15000;XOF;Wave;fake;");

            (await admin.Client.GetAsync($"{Admin}/finance/summary?from=2026-01-01&to=2020-01-01")).StatusCode.Should().Be(HttpStatusCode.BadRequest);
        }
        finally
        {
            await TestCleanup.DeleteEstablishmentsAsync(api, salon.Owner.UserId);
        }
    }

    [Fact]
    public async Task An_unpaid_subscription_can_be_reminded_manually_but_not_in_a_loop()
    {
        using var admin = await TestAccounts.AdminAsync(api);
        var late = await TestSalons.PublishAsync(api, admin);
        var fine = await TestSalons.PublishAsync(api, admin);
        try
        {
            await WithDbAsync(db => db.Subscriptions.Where(s => s.EstablishmentId == late.Id)
                .ExecuteUpdateAsync(set => set.SetProperty(s => s.CurrentPeriodEnd, api.Clock.GetUtcNow().AddDays(-10))));
            var maintenance = api.Services.GetRequiredService<BillingMaintenance>();
            await maintenance.RunOnceAsync(CancellationToken.None);

            var summary = (await admin.Client.GetFromJsonAsync<FinanceSummary>($"{Admin}/finance/summary"))!;
            var line = summary.PastDue.Single(p => p.EstablishmentId == late.Id);
            line.DaysLate.Should().BeGreaterThanOrEqualTo(10);
            line.OwnerEmail.Should().Be(late.Owner.Email);

            var remind = $"{Admin}/finance/past-due/{line.SubscriptionId}/remind";
            (await admin.Client.PostAsync(remind, null)).StatusCode.Should().Be(HttpStatusCode.NoContent);
            (await late.Owner.Client.GetFromJsonAsync<NotificationList>("/api/v1/me/notifications"))!.Items
                .Should().Contain(n => n.TemplateCode == "billing.manual_reminder");
            api.Emails.Messages.Should().Contain(m => m.To == late.Owner.Email && m.Subject.Contains("Relance", StringComparison.Ordinal));
            (await admin.Client.PostAsync(remind, null)).StatusCode.Should().Be(HttpStatusCode.Conflict, "one reminder per hour");

            var healthy = (await fine.Owner.Client.GetFromJsonAsync<SubscriptionOverview>($"/api/v1/pro/establishments/{fine.Id}/subscription"))!.Subscription!;
            (await admin.Client.PostAsync($"{Admin}/finance/past-due/{healthy.Id}/remind", null)).StatusCode.Should().Be(HttpStatusCode.Conflict);
            (await admin.Client.PostAsync($"{Admin}/finance/past-due/{Guid.NewGuid()}/remind", null)).StatusCode.Should().Be(HttpStatusCode.NotFound);
        }
        finally
        {
            await TestCleanup.DeleteEstablishmentsAsync(api, late.Owner.UserId, fine.Owner.UserId);
        }
    }

    // ───────── Settings (ADM-05) ─────────

    [Fact]
    public async Task Categories_are_created_edited_and_deactivated()
    {
        using var admin = await TestAccounts.AdminAsync(api);
        var name = $"Catégorie test {Guid.NewGuid().ToString("N")[..8]}";
        Guid? id = null;
        try
        {
            (await admin.Client.PostAsJsonAsync($"{Admin}/categories", new { name, icon = "Bad Icon", sortOrder = 90, isActive = true })).StatusCode
                .Should().Be(HttpStatusCode.BadRequest);

            var created = await admin.Client.PostAsJsonAsync($"{Admin}/categories", new { name, icon = "sparkles", sortOrder = 90, isActive = true });
            created.StatusCode.Should().Be(HttpStatusCode.Created);
            id = (await created.Content.ReadFromJsonAsync<CreatedId>())!.Id;
            (await admin.Client.PostAsJsonAsync($"{Admin}/categories", new { name, icon = "sparkles", sortOrder = 91, isActive = true })).StatusCode
                .Should().Be(HttpStatusCode.Conflict);

            using var anonymous = api.CreateClient();
            (await anonymous.GetFromJsonAsync<List<CategoryDto>>("/api/v1/categories"))!.Should().Contain(c => c.Id == id);

            (await admin.Client.PutAsJsonAsync($"{Admin}/categories/{id}", new { name, icon = "gem", sortOrder = 95, isActive = false })).StatusCode
                .Should().Be(HttpStatusCode.NoContent);
            (await anonymous.GetFromJsonAsync<List<CategoryDto>>("/api/v1/categories"))!.Should().NotContain(c => c.Id == id);
            (await admin.Client.GetFromJsonAsync<List<CategoryAdminDto>>($"{Admin}/categories"))!.Single(c => c.Id == id)
                .Should().Match<CategoryAdminDto>(c => !c.IsActive && c.Icon == "gem" && c.Slug.StartsWith("categorie-test-", StringComparison.Ordinal));
            (await admin.Client.PutAsJsonAsync($"{Admin}/categories/{Guid.NewGuid()}", new { name = "X Y", icon = "gem", sortOrder = 1, isActive = true })).StatusCode
                .Should().Be(HttpStatusCode.NotFound);
        }
        finally
        {
            if (id is { } categoryId)
            {
                await WithDbAsync(db => db.BeautyCategories.Where(c => c.Id == categoryId).ExecuteDeleteAsync());
            }
        }
    }

    [Fact]
    public async Task Geographic_zones_can_be_added_and_switched_off()
    {
        using var admin = await TestAccounts.AdminAsync(api);
        var suffix = Guid.NewGuid().ToString("N")[..6];
        var countries = (await admin.Client.GetFromJsonAsync<List<GeoAdminDto>>($"{Admin}/geo/countries"))!;
        var ivoryCoast = countries.Single(c => c.Code == "CI");
        try
        {
            (await admin.Client.GetAsync($"{Admin}/geo/regions")).StatusCode.Should().Be(HttpStatusCode.BadRequest);
            (await admin.Client.GetAsync($"{Admin}/geo/planets")).StatusCode.Should().Be(HttpStatusCode.NotFound);

            var region = await CreateGeoAsync(admin, "regions", ivoryCoast.Id, $"Région test {suffix}");
            var city = await CreateGeoAsync(admin, "cities", region, $"Ville test {suffix}");
            var commune = await CreateGeoAsync(admin, "communes", city, $"Commune test {suffix}", 5.3, -4.0);
            (await admin.Client.PostAsJsonAsync($"{Admin}/geo/communes", new { parentId = city, name = $"commune TEST {suffix}" })).StatusCode
                .Should().Be(HttpStatusCode.Conflict);
            (await admin.Client.PostAsJsonAsync($"{Admin}/geo/communes", new { parentId = city, name = "Hors limites", latitude = 120.0, longitude = 0.0 })).StatusCode
                .Should().Be(HttpStatusCode.BadRequest);

            using var anonymous = api.CreateClient();
            (await anonymous.GetFromJsonAsync<List<GeoItemDto>>($"/api/v1/geo/cities/{city}/communes"))!.Should().ContainSingle(c => c.Id == commune);

            (await admin.Client.PutAsJsonAsync($"{Admin}/geo/communes/{commune}", new { name = $"Commune test {suffix}", isActive = false })).StatusCode
                .Should().Be(HttpStatusCode.NoContent);
            (await anonymous.GetFromJsonAsync<List<GeoItemDto>>($"/api/v1/geo/cities/{city}/communes"))!.Should().BeEmpty();
            (await admin.Client.GetFromJsonAsync<List<GeoAdminDto>>($"{Admin}/geo/communes?parentId={city}"))!.Single().IsActive.Should().BeFalse();
        }
        finally
        {
            await WithDbAsync(async db =>
            {
                var regions = db.Regions.Where(r => r.Name == $"Région test {suffix}").Select(r => r.Id);
                var cities = db.Cities.Where(c => regions.Contains(EF.Property<Guid>(c, "RegionId"))).Select(c => c.Id);
                await db.Communes.Where(c => cities.Contains(EF.Property<Guid>(c, "CityId"))).ExecuteDeleteAsync();
                await db.Cities.Where(c => cities.Contains(c.Id)).ExecuteDeleteAsync();
                await db.Regions.Where(r => r.Name == $"Région test {suffix}").ExecuteDeleteAsync();
            });
        }
    }

    private static async Task<Guid> CreateGeoAsync(UserSession admin, string level, Guid parent, string name, double? lat = null, double? lng = null)
    {
        var response = await admin.Client.PostAsJsonAsync($"{Admin}/geo/{level}", new { parentId = parent, name, latitude = lat, longitude = lng });
        response.StatusCode.Should().Be(HttpStatusCode.Created, await response.Content.ReadAsStringAsync());
        return (await response.Content.ReadFromJsonAsync<CreatedId>())!.Id;
    }

    [Fact]
    public async Task The_grace_period_is_a_validated_global_setting_used_by_the_billing_job()
    {
        using var admin = await TestAccounts.AdminAsync(api);
        try
        {
            (await admin.Client.PutAsJsonAsync($"{Admin}/settings", new { graceDays = 40 })).StatusCode.Should().Be(HttpStatusCode.BadRequest);
            (await admin.Client.PutAsJsonAsync($"{Admin}/settings", new { graceDays = 3 })).StatusCode.Should().Be(HttpStatusCode.OK);
            (await admin.Client.GetFromJsonAsync<AppSettingsDto>($"{Admin}/settings"))!.GraceDays.Should().Be(3);

            await using var scope = api.Services.CreateAsyncScope();
            (await scope.ServiceProvider.GetRequiredService<AppSettingsService>().GraceDaysAsync(CancellationToken.None)).Should().Be(3);
        }
        finally
        {
            await WithDbAsync(db => db.AppSettings.Where(s => s.Key == AppSettingsService.GraceDaysKey).ExecuteDeleteAsync());
        }
    }

    // ───────── Message templates (NOT-02) ─────────

    [Fact]
    public async Task An_edited_template_replaces_the_built_in_message_until_it_is_reset()
    {
        using var admin = await TestAccounts.AdminAsync(api);
        var catalog = (await admin.Client.GetFromJsonAsync<List<TemplateDto>>($"{Admin}/templates"))!;
        catalog.Should().Contain(t => t.Code == "billing.payment_succeeded" && !t.IsCustom && t.Variables.Contains("montant"));
        var url = $"{Admin}/templates/billing.payment_succeeded";
        var salon = await TestSalons.PublishAsync(api, admin, subscribe: false);
        try
        {
            (await admin.Client.PutAsJsonAsync(url, new { title = "Merci", body = "Bonjour {inconnu}" })).StatusCode.Should().Be(HttpStatusCode.BadRequest);
            (await admin.Client.PutAsJsonAsync(url, new { title = "Merci", body = "Accolade {facture" })).StatusCode.Should().Be(HttpStatusCode.BadRequest);
            (await admin.Client.PutAsJsonAsync($"{Admin}/templates/nope", new { title = "A", body = "B" })).StatusCode.Should().Be(HttpStatusCode.NotFound);

            (await admin.Client.PutAsJsonAsync(url, new { title = "Merci {etablissement} !", body = "Reçu {montant} (facture {facture})." })).StatusCode
                .Should().Be(HttpStatusCode.NoContent);
            await TestSalons.SubscribePremiumAsync(salon.Owner, salon.Id);

            var notification = (await salon.Owner.Client.GetFromJsonAsync<NotificationList>("/api/v1/me/notifications"))!.Items
                .Single(n => n.TemplateCode == "billing.payment_succeeded");
            notification.Title.Should().StartWith("Merci Salon Rdv");
            notification.Body.Should().StartWith("Reçu 15").And.Contain("XOF").And.Contain("(facture KB-");

            (await admin.Client.DeleteAsync(url)).StatusCode.Should().Be(HttpStatusCode.NoContent);
            (await admin.Client.GetFromJsonAsync<List<TemplateDto>>($"{Admin}/templates"))!.Single(t => t.Code == "billing.payment_succeeded").IsCustom.Should().BeFalse();
        }
        finally
        {
            await WithDbAsync(db => db.NotificationTemplates.Where(t => t.Code == "billing.payment_succeeded").ExecuteDeleteAsync());
            await TestCleanup.DeleteEstablishmentsAsync(api, salon.Owner.UserId);
        }
    }
}
