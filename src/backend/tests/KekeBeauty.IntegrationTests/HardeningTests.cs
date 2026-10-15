using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using KekeBeauty.Infrastructure.Persistence;
using KekeBeauty.Infrastructure.Privacy;
using KekeBeauty.Infrastructure.Scheduling;
using KekeBeauty.Modules.Identity.Application;
using KekeBeauty.Modules.Identity.Domain;
using KekeBeauty.Modules.Scheduling.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace KekeBeauty.IntegrationTests;

[Collection(ApiCollection.Name)]
public sealed class HardeningTests(ApiFixture api)
{
    private const string Phone = "07 00 00 55 66";

    private async Task WithDbAsync(Func<KekeBeautyDbContext, Task> action)
    {
        await using var scope = api.Services.CreateAsyncScope();
        await action(scope.ServiceProvider.GetRequiredService<KekeBeautyDbContext>());
    }

    // ───────── Headers ─────────

    [Fact]
    public async Task Every_response_carries_the_security_headers_and_a_request_id()
    {
        using var client = api.CreateClient();

        foreach (var path in new[] { "/health/live", "/api/v1/categories", "/api/v1/does-not-exist" })
        {
            var response = await client.GetAsync(path);
            response.Headers.GetValues("X-Content-Type-Options").Should().Equal("nosniff");
            response.Headers.GetValues("X-Frame-Options").Should().Equal("DENY");
            response.Headers.GetValues("Referrer-Policy").Should().Equal("strict-origin-when-cross-origin");
            response.Headers.Contains("Permissions-Policy").Should().BeTrue(path);
            response.Headers.GetValues("X-Request-Id").Single().Should().NotBeNullOrWhiteSpace();
        }

        (await client.GetAsync("/api/v1/categories")).Headers.CacheControl!.NoStore.Should().BeTrue("API answers are never cached by shared caches");
    }

    // ───────── Consent ─────────

    [Fact]
    public async Task Registration_requires_consent_and_keeps_the_proof()
    {
        using var client = api.CreateClient();
        var email = $"consent{Guid.NewGuid():N}@example.com";

        var refused = await client.PostAsJsonAsync("/api/v1/auth/register", new RegisterRequest(email, TestAccounts.Password, "Awa", "Koné", "Client"));
        refused.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await refused.Content.ReadAsStringAsync()).Should().Contain("auth.terms_required");
        await WithDbAsync(async db => (await db.Users.AnyAsync(u => u.Email == email)).Should().BeFalse());

        (await client.PostAsJsonAsync("/api/v1/auth/register", new RegisterRequest(email, TestAccounts.Password, "Awa", "Koné", "Client", true)))
            .StatusCode.Should().Be(HttpStatusCode.Accepted);
        await WithDbAsync(async db => (await db.Users.SingleAsync(u => u.Email == email)).TermsAcceptedAt.Should().NotBeNull());
    }

    // ───────── Right of access ─────────

    [Fact]
    public async Task A_person_can_download_everything_held_about_them()
    {
        using var admin = await TestAccounts.AdminAsync(api);
        var salon = await TestSalons.PublishAsync(api, admin);
        using var client = await TestAccounts.ClientAsync(api);
        try
        {
            var booked = await client.Client.PostAsJsonAsync("/api/v1/appointments",
                new BookingRequest(salon.Id, [salon.ServiceHour], null, TestSalons.NextOpenDay(api), Phone, "Allergie au latex"));
            booked.EnsureSuccessStatusCode();

            var response = await client.Client.GetAsync("/api/v1/me/export");
            response.StatusCode.Should().Be(HttpStatusCode.OK);
            response.Content.Headers.ContentDisposition!.FileName.Should().Be("mes-donnees-keke-beauty.json");
            var export = (await response.Content.ReadFromJsonAsync<PersonalDataExport>(new JsonSerializerOptions(JsonSerializerDefaults.Web)))!;

            export.Email.Should().Be(client.Email);
            export.TermsAcceptedAt.Should().NotBeNull();
            export.Appointments.Should().ContainSingle().Which.Should().Match<ExportedAppointment>(a => a.Phone != null && a.Phone.EndsWith("5566", StringComparison.Ordinal) && a.Note == "Allergie au latex");
            export.Notifications.Should().NotBeEmpty();

            (await salon.Owner.Client.GetFromJsonAsync<PersonalDataExport>("/api/v1/me/export"))!.Establishments.Should().ContainSingle(e => e.Id == salon.Id);
            await WithDbAsync(async db => (await db.AuditLogs.AnyAsync(a => a.Action == "privacy.export" && a.ActorUserId == client.UserId)).Should().BeTrue());

            using var anonymous = api.CreateClient();
            (await anonymous.GetAsync("/api/v1/me/export")).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        }
        finally
        {
            await TestCleanup.DeleteEstablishmentsAsync(api, salon.Owner.UserId);
        }
    }

    // ───────── Right to erasure ─────────

    [Fact]
    public async Task Deleting_an_account_erases_personal_data_but_only_when_nothing_is_pending()
    {
        using var admin = await TestAccounts.AdminAsync(api);
        var salon = await TestSalons.PublishAsync(api, admin);
        using var client = await TestAccounts.ClientAsync(api);
        try
        {
            var booked = await client.Client.PostAsJsonAsync("/api/v1/appointments",
                new BookingRequest(salon.Id, [salon.ServiceHour], null, TestSalons.NextOpenDay(api), Phone, "Note privée"));
            booked.EnsureSuccessStatusCode();

            (await client.Client.PostAsJsonAsync("/api/v1/me/delete", new { password = "Wrong-Password-1" })).StatusCode.Should().Be(HttpStatusCode.BadRequest);
            var blocked = await client.Client.PostAsJsonAsync("/api/v1/me/delete", new { password = TestAccounts.Password });
            blocked.StatusCode.Should().Be(HttpStatusCode.Conflict);
            (await blocked.Content.ReadAsStringAsync()).Should().Contain("account.has_upcoming_appointments");

            // A business owner with a published listing must close it first; an admin is managed by a SuperAdmin.
            (await salon.Owner.Client.PostAsJsonAsync("/api/v1/me/delete", new { password = TestAccounts.Password })).StatusCode.Should().Be(HttpStatusCode.Conflict);
            (await admin.Client.PostAsJsonAsync("/api/v1/me/delete", new { password = TestAccounts.Password })).StatusCode.Should().Be(HttpStatusCode.Forbidden);

            await WithDbAsync(db => db.Appointments.Where(a => a.ClientUserId == client.UserId)
                .ExecuteUpdateAsync(set => set.SetProperty(a => a.Status, AppointmentStatus.Completed)));
            (await client.Client.PostAsJsonAsync("/api/v1/me/delete", new { password = TestAccounts.Password })).StatusCode.Should().Be(HttpStatusCode.NoContent);

            await WithDbAsync(async db =>
            {
                var user = await db.Users.AsNoTracking().SingleAsync(u => u.Id == client.UserId);
                user.Email.Should().EndWith("@deleted.invalid").And.NotContain(client.Email);
                user.FirstName.Should().Be("Compte");
                user.PasswordHash.Should().BeNull();
                user.Status.Should().Be(UserStatus.Suspended);
                user.DeletedAt.Should().NotBeNull();

                var appointment = await db.Appointments.AsNoTracking().SingleAsync(a => a.ClientUserId == client.UserId);
                appointment.ClientPhone.Should().BeNull();
                appointment.ClientNote.Should().BeNull();
                (await db.Notifications.AnyAsync(n => n.UserId == client.UserId)).Should().BeFalse();
                (await db.RefreshTokens.AnyAsync(t => t.UserId == client.UserId)).Should().BeFalse();
            });

            using var anonymous = api.CreateClient();
            (await anonymous.PostAsJsonAsync("/api/v1/auth/login", new LoginRequest(client.Email, TestAccounts.Password))).IsSuccessStatusCode.Should().BeFalse();
        }
        finally
        {
            await TestCleanup.DeleteEstablishmentsAsync(api, salon.Owner.UserId);
        }
    }
}
