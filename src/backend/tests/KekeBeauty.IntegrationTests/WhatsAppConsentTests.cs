using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using KekeBeauty.Infrastructure.Admin;
using KekeBeauty.Infrastructure.Notifications;
using KekeBeauty.Infrastructure.Persistence;
using KekeBeauty.Infrastructure.Scheduling;
using KekeBeauty.Modules.Notifications.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace KekeBeauty.IntegrationTests;

/// <summary>Test helpers: a client with a linked WhatsApp number (as after the code check) and consent given the way the screen does.</summary>
internal static class TestConsent
{
    public static string NewPhone() => $"+22507{Random.Shared.Next(10_000_000, 99_999_999)}";

    public static async Task LinkAsync(ApiFixture api, Guid userId, string phone)
    {
        await using var scope = api.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<KekeBeautyDbContext>();
        var now = api.Clock.GetUtcNow();
        await db.Users.Where(u => u.Id == userId).ExecuteUpdateAsync(set => set.SetProperty(u => u.PhoneNumber, phone).SetProperty(u => u.PhoneVerifiedAt, now));
    }

    public static async Task GrantAsync(ApiFixture api, Guid userId, ConsentPurpose purpose)
    {
        await using var scope = api.Services.CreateAsyncScope();
        var result = await scope.ServiceProvider.GetRequiredService<WhatsAppConsentService>()
            .SetAsync(userId, new WhatsAppConsentInput(purpose.ToString(), true, WhatsAppConsentTexts.Version), "settings", CancellationToken.None);
        result.IsSuccess.Should().BeTrue();
    }
}

[Collection(ApiCollection.Name)]
public sealed class WhatsAppConsentTests(ApiFixture api)
{
    private static readonly Dictionary<string, string> Variables = new()
    {
        ["etablissement"] = "Maison Éclat Coiffure",
        ["client"] = "Awa Koné",
        ["date"] = "12/11/2026",
        ["heure"] = "10:00",
        ["motif"] = string.Empty,
    };

    private async Task ResetAsync()
    {
        await using var scope = api.Services.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<KekeBeautyDbContext>().AppSettings.ExecuteDeleteAsync();
        await TestChannels.SetAsync(api, AppSettingsService.WhatsAppEnabledKey, true);
    }

    private async Task NotifyAsync(Guid userId, string phone, string code = "appointment.confirmed", IReadOnlyDictionary<string, string>? variables = null)
    {
        await using var scope = api.Services.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<NotificationService>()
            .NotifyAsync(userId, code, "Titre", "Corps", phone, CancellationToken.None, variables ?? Variables);
    }

    private static Task<HttpResponseMessage> PutAsync(HttpClient client, string purpose, bool granted, string? version = null) =>
        client.PutAsJsonAsync("/api/v1/me/whatsapp-consent", new WhatsAppConsentInput(purpose, granted, version ?? WhatsAppConsentTexts.Version));

    private static Task<WhatsAppConsentStatus?> StatusAsync(HttpClient client) => client.GetFromJsonAsync<WhatsAppConsentStatus>("/api/v1/me/whatsapp-consent");

    [Fact]
    public async Task Consent_needs_a_signed_in_user_a_linked_number_and_the_text_that_is_shown()
    {
        using var anonymous = api.CreateClient();
        (await anonymous.GetAsync("/api/v1/me/whatsapp-consent")).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        (await anonymous.PutAsJsonAsync("/api/v1/me/whatsapp-consent", new WhatsAppConsentInput("Service", true, WhatsAppConsentTexts.Version))).StatusCode.Should().Be(HttpStatusCode.Unauthorized);

        using var user = await TestAccounts.ClientAsync(api);
        var before = await StatusAsync(user.Client);
        before!.Linked.Should().BeFalse();
        before.Service.Granted.Should().BeFalse();
        before.ServiceText.Should().Contain("STOP").And.Contain("WhatsApp");
        before.MarketingText.Should().Contain("offres");

        (await PutAsync(user.Client, "Service", true)).StatusCode.Should().Be(HttpStatusCode.Conflict);

        await TestConsent.LinkAsync(api, user.UserId, TestConsent.NewPhone());
        (await PutAsync(user.Client, "Service", true, version: "1999-01-01")).StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await PutAsync(user.Client, "Newsletter", true)).StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await PutAsync(user.Client, "Service", true)).StatusCode.Should().Be(HttpStatusCode.OK);

        var after = await StatusAsync(user.Client);
        after!.Service.Should().Match<ConsentState>(s => s.Granted && s.At != null);
        after.Marketing.Granted.Should().BeFalse("the marketing agreement is separate and off by default");
    }

    [Fact]
    public async Task Every_agreement_and_withdrawal_is_kept_as_proof_with_the_exact_text_and_never_rewritten()
    {
        using var user = await TestAccounts.ClientAsync(api);
        var phone = TestConsent.NewPhone();
        await TestConsent.LinkAsync(api, user.UserId, phone);

        (await PutAsync(user.Client, "Service", true)).EnsureSuccessStatusCode();
        (await PutAsync(user.Client, "Service", true)).EnsureSuccessStatusCode();
        (await PutAsync(user.Client, "Service", false)).EnsureSuccessStatusCode();
        (await PutAsync(user.Client, "Marketing", true)).EnsureSuccessStatusCode();

        await using var scope = api.Services.CreateAsyncScope();
        var rows = await scope.ServiceProvider.GetRequiredService<KekeBeautyDbContext>().WhatsAppConsents.AsNoTracking()
            .Where(c => c.UserId == user.UserId).OrderBy(c => c.At).ToListAsync();
        var service = rows.Where(r => r.Purpose == ConsentPurpose.Service).ToList();
        var marketing = rows.Where(r => r.Purpose == ConsentPurpose.Marketing).ToList();
        service.Select(r => r.Granted).Should().Equal(true, false);
        marketing.Select(r => r.Granted).Should().Equal(true);
        service[1].At.Should().BeAfter(service[0].At, "the latest row of a kind is never ambiguous");
        rows.Should().OnlyContain(r => r.Phone == phone && r.TextVersion == WhatsAppConsentTexts.Version && r.Source == "settings");
        service[0].TextSnapshot.Should().Be(WhatsAppConsentTexts.Service);
        marketing[0].TextSnapshot.Should().Be(WhatsAppConsentTexts.Marketing);
    }

    [Fact]
    public async Task One_person_can_neither_read_nor_change_the_agreement_of_another()
    {
        using var alice = await TestAccounts.ClientAsync(api);
        using var bob = await TestAccounts.ClientAsync(api);
        await TestConsent.LinkAsync(api, alice.UserId, TestConsent.NewPhone());
        await TestConsent.LinkAsync(api, bob.UserId, TestConsent.NewPhone());
        await TestConsent.GrantAsync(api, alice.UserId, ConsentPurpose.Service);

        (await StatusAsync(bob.Client))!.Service.Granted.Should().BeFalse();
        (await PutAsync(bob.Client, "Service", false)).EnsureSuccessStatusCode();
        (await StatusAsync(alice.Client))!.Service.Granted.Should().BeTrue();
    }

    [Fact]
    public async Task A_message_goes_out_by_whatsapp_only_with_a_template_all_its_variables_and_the_agreement()
    {
        await ResetAsync();
        var whatsApp = api.Services.GetRequiredService<CapturingWhatsAppSender>();
        using var user = await TestAccounts.ClientAsync(api);
        var phone = TestConsent.NewPhone();
        await TestConsent.LinkAsync(api, user.UserId, phone);
        try
        {
            // No agreement yet: nothing is sent, however well the message is formed.
            var before = whatsApp.Messages.Count(m => m.Phone == phone);
            await NotifyAsync(user.UserId, phone);
            whatsApp.Messages.Count(m => m.Phone == phone).Should().Be(before);

            await TestConsent.GrantAsync(api, user.UserId, ConsentPurpose.Service);
            await NotifyAsync(user.UserId, phone);
            var sent = whatsApp.Messages.Where(m => m.Phone == phone).Should().ContainSingle().Subject;
            sent.Template.Should().Be("rdv_confirme");
            sent.Parameters.Should().Equal("Awa", "Maison Éclat Coiffure", "12/11/2026", "10:00");

            // A message without a template, a message without its variables, and a different number are all silent.
            await NotifyAsync(user.UserId, phone, "appointment.confirmed.partner");
            await NotifyAsync(user.UserId, phone, variables: new Dictionary<string, string> { ["etablissement"] = "Maison Éclat Coiffure" });
            await NotifyAsync(user.UserId, TestConsent.NewPhone());
            whatsApp.Messages.Count(m => m.Phone == phone).Should().Be(1);
        }
        finally
        {
            await ResetAsync();
            await using var scope = api.Services.CreateAsyncScope();
            await scope.ServiceProvider.GetRequiredService<KekeBeautyDbContext>().AppSettings.ExecuteDeleteAsync();
        }
    }

    [Fact]
    public async Task The_marketing_invitation_needs_its_own_agreement_and_the_service_agreement_does_not_cover_it()
    {
        await ResetAsync();
        var whatsApp = api.Services.GetRequiredService<CapturingWhatsAppSender>();
        using var user = await TestAccounts.ClientAsync(api);
        var phone = TestConsent.NewPhone();
        await TestConsent.LinkAsync(api, user.UserId, phone);
        var variables = new Dictionary<string, string> { ["etablissement"] = "Maison Éclat Coiffure", ["client"] = string.Empty, ["date"] = string.Empty, ["heure"] = string.Empty, ["motif"] = string.Empty };
        try
        {
            await TestConsent.GrantAsync(api, user.UserId, ConsentPurpose.Service);
            await NotifyAsync(user.UserId, phone, "client.reengagement", variables);
            whatsApp.Messages.Count(m => m.Phone == phone).Should().Be(0);

            await TestConsent.GrantAsync(api, user.UserId, ConsentPurpose.Marketing);
            await NotifyAsync(user.UserId, phone, "client.reengagement", variables);
            whatsApp.Messages.Where(m => m.Phone == phone).Should().ContainSingle().Which.Template.Should().Be("keke_retour");

            // Withdrawing only the marketing agreement keeps the service messages.
            (await PutAsync(user.Client, "Marketing", false)).EnsureSuccessStatusCode();
            await NotifyAsync(user.UserId, phone, "client.reengagement", variables);
            await NotifyAsync(user.UserId, phone);
            whatsApp.Messages.Where(m => m.Phone == phone).Select(m => m.Template).Should().Equal("keke_retour", "rdv_confirme");
        }
        finally
        {
            await ResetAsync();
            await using var scope = api.Services.CreateAsyncScope();
            await scope.ServiceProvider.GetRequiredService<KekeBeautyDbContext>().AppSettings.ExecuteDeleteAsync();
        }
    }

    [Fact]
    public async Task Linking_another_number_or_the_same_one_again_starts_from_nothing()
    {
        using var user = await TestAccounts.ClientAsync(api);
        await TestConsent.LinkAsync(api, user.UserId, TestConsent.NewPhone());
        await TestConsent.GrantAsync(api, user.UserId, ConsentPurpose.Service);
        (await StatusAsync(user.Client))!.Service.Granted.Should().BeTrue();

        api.Clock.Advance(TimeSpan.FromMinutes(1));
        await TestConsent.LinkAsync(api, user.UserId, TestConsent.NewPhone());
        (await StatusAsync(user.Client))!.Service.Granted.Should().BeFalse("an agreement was given for the previous number");

        api.Clock.Advance(TimeSpan.FromMinutes(1));
        await TestConsent.GrantAsync(api, user.UserId, ConsentPurpose.Service);
        var again = await StatusAsync(user.Client);
        again!.Service.Granted.Should().BeTrue();
        await using var scope = api.Services.CreateAsyncScope();
        (await scope.ServiceProvider.GetRequiredService<KekeBeautyDbContext>().WhatsAppConsents.CountAsync(c => c.UserId == user.UserId)).Should().Be(2, "both agreements stay in the register");
    }

    [Fact]
    public async Task A_stop_reply_closes_every_agreement_of_the_number_and_the_register_says_so()
    {
        using var user = await TestAccounts.ClientAsync(api);
        var phone = TestConsent.NewPhone();
        await TestConsent.LinkAsync(api, user.UserId, phone);
        await TestConsent.GrantAsync(api, user.UserId, ConsentPurpose.Service);
        await TestConsent.GrantAsync(api, user.UserId, ConsentPurpose.Marketing);

        await using var scope = api.Services.CreateAsyncScope();
        var withdrawn = await scope.ServiceProvider.GetRequiredService<WhatsAppConsentService>().WithdrawAllAsync(user.UserId, phone, "stop", CancellationToken.None);

        withdrawn.Should().Be(2);
        var status = await StatusAsync(user.Client);
        status!.Service.Granted.Should().BeFalse();
        status.Marketing.Granted.Should().BeFalse();
        (await scope.ServiceProvider.GetRequiredService<KekeBeautyDbContext>().WhatsAppConsents.CountAsync(c => c.UserId == user.UserId && c.Source == "stop" && !c.Granted)).Should().Be(2);
    }
}
