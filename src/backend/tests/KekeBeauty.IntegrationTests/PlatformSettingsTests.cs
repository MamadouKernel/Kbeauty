using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using KekeBeauty.Infrastructure.Admin;
using KekeBeauty.Infrastructure.Partner;
using KekeBeauty.Infrastructure.Persistence;
using KekeBeauty.Modules.Identity.Application;
using KekeBeauty.Modules.Onboarding.Domain;
using KekeBeauty.SharedKernel;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace KekeBeauty.IntegrationTests;

/// <summary>Admin-editable parameters: session inactivity, identity-document retention, reminders and channel switches.</summary>
[Collection(ApiCollection.Name)]
public sealed class PlatformSettingsTests(ApiFixture api)
{
    private const string Settings = "/api/v1/admin/settings";

    private async Task ResetSettingsAsync()
    {
        await using var scope = api.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<KekeBeautyDbContext>();
        await db.AppSettings.ExecuteDeleteAsync();
    }

    [Fact]
    public async Task Settings_default_to_sixty_minutes_of_inactivity_and_are_validated_when_saved()
    {
        await ResetSettingsAsync();
        using var admin = await TestAccounts.AdminAsync(api);
        try
        {
            var defaults = (await admin.Client.GetFromJsonAsync<AppSettingsDto>(Settings))!;
            defaults.SessionIdleMinutes.Should().Be(60);
            defaults.KycRetentionDays.Should().Be(365);
            defaults.SmsEnabled.Should().BeFalse("channels without a provider stay off until an admin switches them on");
            defaults.WhatsAppEnabled.Should().BeFalse();

            foreach (var invalid in new[]
            {
                defaults with { SessionIdleMinutes = 2 },
                defaults with { SessionIdleMinutes = 5000 },
                defaults with { KycRetentionDays = 5 },
                defaults with { KycRetentionDays = 99999 },
                defaults with { ReminderFirstHours = 200 },
                defaults with { GraceDays = 99 },
            })
            {
                (await admin.Client.PutAsJsonAsync(Settings, invalid)).StatusCode.Should().Be(HttpStatusCode.BadRequest);
            }

            var saved = defaults with { SessionIdleMinutes = 30, KycRetentionDays = 90, ReminderFirstHours = 48, ReminderSecondHours = 0, PushEnabled = true };
            (await admin.Client.PutAsJsonAsync(Settings, saved)).StatusCode.Should().Be(HttpStatusCode.OK);
            (await admin.Client.GetFromJsonAsync<AppSettingsDto>(Settings)).Should().Be(saved);
        }
        finally
        {
            await ResetSettingsAsync();
        }
    }

    [Fact]
    public async Task A_session_ends_after_the_configured_inactivity_and_slides_while_the_user_is_active()
    {
        using var admin = await TestAccounts.AdminAsync(api);
        using var user = await TestAccounts.ClientAsync(api);
        try
        {
            var current = (await admin.Client.GetFromJsonAsync<AppSettingsDto>(Settings))!;
            (await admin.Client.PutAsJsonAsync(Settings, current with { SessionIdleMinutes = 10 })).StatusCode.Should().Be(HttpStatusCode.OK);

            // Active user: each refresh pushes the end of the session ten minutes further.
            api.Clock.Advance(TimeSpan.FromMinutes(9));
            var first = await user.Client.PostAsync("/api/v1/auth/refresh", null);
            first.StatusCode.Should().Be(HttpStatusCode.OK);
            (await first.Content.ReadFromJsonAsync<AuthResponse>())!.IdleMinutes.Should().Be(10);

            api.Clock.Advance(TimeSpan.FromMinutes(9));
            (await user.Client.PostAsync("/api/v1/auth/refresh", null)).StatusCode.Should().Be(HttpStatusCode.OK);

            // Inactive for more than ten minutes: the next refresh is refused.
            api.Clock.Advance(TimeSpan.FromMinutes(11));
            (await user.Client.PostAsync("/api/v1/auth/refresh", null)).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        }
        finally
        {
            await ResetSettingsAsync();
        }
    }

    [Fact]
    public async Task Identity_documents_are_deleted_once_the_retention_period_has_passed_but_the_approval_stays()
    {
        using var admin = await TestAccounts.AdminAsync(api);
        using var oldPartner = await TestAccounts.PartnerAsync(api);
        using var recentPartner = await TestAccounts.PartnerAsync(api);
        using var pendingPartner = await TestAccounts.PartnerAsync(api);
        var storage = api.Services.GetRequiredService<IFileStorage>();
        var now = api.Clock.GetUtcNow();

        async Task<KycIdentity> SeedAsync(UserSession owner, KycStatus status, DateTimeOffset? reviewedAt)
        {
            var keys = new[] { $"kyc/{Guid.NewGuid():N}/front", $"kyc/{Guid.NewGuid():N}/back" };
            foreach (var key in keys)
            {
                await storage.PutAsync(StorageBuckets.PrivateKyc, key, new MemoryStream([1, 2, 3]), "image/png");
            }

            await using var scope = api.Services.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<KekeBeautyDbContext>();
            var identity = new KycIdentity
            {
                UserId = owner.UserId,
                HolderName = "Awa Koné",
                DocumentNumber = $"CI{Guid.NewGuid():N}"[..14],
                FrontKey = keys[0],
                BackKey = keys[1],
                Status = status,
                ReviewedAt = reviewedAt,
            };
            db.KycIdentities.Add(identity);
            await db.SaveChangesAsync();
            return identity;
        }

        try
        {
            var current = (await admin.Client.GetFromJsonAsync<AppSettingsDto>(Settings))!;
            (await admin.Client.PutAsJsonAsync(Settings, current with { KycRetentionDays = 90 })).StatusCode.Should().Be(HttpStatusCode.OK);

            var old = await SeedAsync(oldPartner, KycStatus.Approved, now.AddDays(-120));
            var recent = await SeedAsync(recentPartner, KycStatus.Approved, now.AddDays(-10));
            var pending = await SeedAsync(pendingPartner, KycStatus.PendingReview, null);

            var purged = await api.Services.GetRequiredService<RetentionMaintenance>().RunOnceAsync(CancellationToken.None);

            purged.Should().BeGreaterThanOrEqualTo(1);
            await using var scope = api.Services.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<KekeBeautyDbContext>();
            var oldNow = await db.KycIdentities.AsNoTracking().SingleAsync(k => k.Id == old.Id);
            oldNow.FrontKey.Should().BeNull();
            oldNow.BackKey.Should().BeNull();
            oldNow.DocumentsPurgedAt.Should().NotBeNull();
            oldNow.Status.Should().Be(KycStatus.Approved, "the approval is kept so the identity can be reused");
            (await storage.OpenReadAsync(StorageBuckets.PrivateKyc, old.FrontKey!)).Should().BeNull();

            (await db.KycIdentities.AsNoTracking().SingleAsync(k => k.Id == recent.Id)).FrontKey.Should().NotBeNull("still inside the retention period");
            (await db.KycIdentities.AsNoTracking().SingleAsync(k => k.Id == pending.Id)).FrontKey.Should().NotBeNull("a file under review is never purged");

            // A second run has nothing left to do.
            (await api.Services.GetRequiredService<RetentionMaintenance>().RunOnceAsync(CancellationToken.None)).Should().Be(0);
        }
        finally
        {
            await ResetSettingsAsync();
        }
    }

    // ───────── Legal texts ─────────

    private async Task ReseedLegalAsync()
    {
        await using var scope = api.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<KekeBeautyDbContext>();
        await db.LegalDocumentVersions.ExecuteDeleteAsync();
        await db.LegalDocuments.ExecuteDeleteAsync();
        await LegalDocumentService.SeedAsync(db, TimeProvider.System, CancellationToken.None);
    }

    [Fact]
    public async Task Visitors_read_the_published_legal_texts_and_the_unfinished_legal_notice_stays_hidden()
    {
        using var anonymous = api.CreateClient();

        var terms = await anonymous.GetAsync("/api/v1/legal/cgu");
        terms.StatusCode.Should().Be(HttpStatusCode.OK);
        (await terms.Content.ReadFromJsonAsync<LegalDocumentDto>())!.Body.Should().Contain("## 1. Objet");
        (await anonymous.GetAsync("/api/v1/legal/confidentialite")).StatusCode.Should().Be(HttpStatusCode.OK);
        (await anonymous.GetAsync("/api/v1/legal/mentions-legales")).StatusCode.Should().Be(HttpStatusCode.NotFound, "it still holds placeholders");
        (await anonymous.GetAsync("/api/v1/legal/nimporte-quoi")).StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Admins_edit_and_publish_legal_texts_with_a_version_history_and_cannot_publish_placeholders()
    {
        using var admin = await TestAccounts.AdminAsync(api);
        using var client = await TestAccounts.ClientAsync(api);
        using var anonymous = api.CreateClient();
        try
        {
            (await client.Client.GetAsync("/api/v1/admin/legal")).StatusCode.Should().Be(HttpStatusCode.Forbidden);
            (await anonymous.GetAsync("/api/v1/admin/legal")).StatusCode.Should().Be(HttpStatusCode.Unauthorized);

            var notice = (await admin.Client.GetFromJsonAsync<LegalDocumentAdminDto>("/api/v1/admin/legal/mentions-legales"))!;
            notice.IsPublished.Should().BeFalse();
            (await admin.Client.PutAsJsonAsync("/api/v1/admin/legal/mentions-legales", new LegalDocumentInput(notice.Title, notice.Body, true)))
                .StatusCode.Should().Be(HttpStatusCode.BadRequest, "placeholders must be filled in before publishing");

            const string body = """
                ## Éditeur du site

                - Dénomination sociale : Keke Beauty SARL
                - Siège social : Abidjan, Cocody

                Contact : contact@keke.test
                """;
            var saved = await admin.Client.PutAsJsonAsync("/api/v1/admin/legal/mentions-legales", new LegalDocumentInput("Mentions légales", body, true));
            saved.StatusCode.Should().Be(HttpStatusCode.OK);
            (await saved.Content.ReadFromJsonAsync<LegalDocumentAdminDto>())!.Version.Should().Be(2);

            var visible = await anonymous.GetFromJsonAsync<LegalDocumentDto>("/api/v1/legal/mentions-legales");
            visible!.Body.Should().Contain("Keke Beauty SARL");

            var versions = (await admin.Client.GetFromJsonAsync<List<LegalVersionDto>>("/api/v1/admin/legal/mentions-legales/versions"))!;
            versions.Select(v => v.Version).Should().Equal(2, 1);

            // Un-publishing hides it again, and bad input is refused.
            (await admin.Client.PutAsJsonAsync("/api/v1/admin/legal/mentions-legales", new LegalDocumentInput("Mentions légales", body, false))).StatusCode.Should().Be(HttpStatusCode.OK);
            (await anonymous.GetAsync("/api/v1/legal/mentions-legales")).StatusCode.Should().Be(HttpStatusCode.NotFound);
            (await admin.Client.PutAsJsonAsync("/api/v1/admin/legal/cgu", new LegalDocumentInput("x", "court", true))).StatusCode.Should().Be(HttpStatusCode.BadRequest);
            (await admin.Client.PutAsJsonAsync("/api/v1/admin/legal/inconnu", new LegalDocumentInput("Titre valide", body, true))).StatusCode.Should().Be(HttpStatusCode.NotFound);
        }
        finally
        {
            await ReseedLegalAsync();
        }
    }
}
