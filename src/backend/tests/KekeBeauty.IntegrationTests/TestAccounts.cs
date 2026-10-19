using FluentAssertions;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.RegularExpressions;
using KekeBeauty.Infrastructure.Persistence;
using KekeBeauty.Modules.Identity.Application;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using OtpNet;
using SkiaSharp;

namespace KekeBeauty.IntegrationTests;

/// <summary>A signed-in test user (confirmed e-mail, Google Authenticator enrolled) with an authorised HTTP client.</summary>
internal sealed record UserSession(HttpClient Client, Guid UserId, string Email) : IDisposable
{
    public void Dispose() => Client.Dispose();
}

/// <summary>Creates throw-away accounts through the real registration and 2FA endpoints.</summary>
internal static partial class TestAccounts
{
    public const string Password = "Correct-Horse-42";

    [GeneratedRegex(@"token=([^\s&]+)")]
    private static partial Regex TokenPattern();

    public static Task<UserSession> ClientAsync(ApiFixture api) => SignUpAsync(api, "Client", promoteTo: null);

    public static Task<UserSession> PartnerAsync(ApiFixture api, string firstName = "Awa", string lastName = "Koné") =>
        SignUpAsync(api, "PartnerOwner", promoteTo: null, firstName, lastName);

    /// <summary>A collaborator account (role PartnerStaff), as it is once an owner has linked it to a resource.</summary>
    public static Task<UserSession> StaffAsync(ApiFixture api) => SignUpAsync(api, "Client", promoteTo: "PartnerStaff");

    /// <summary>A fresh Admin (the seeded SuperAdmin is left alone: another test enrols it and expects a first login).</summary>
    public static Task<UserSession> AdminAsync(ApiFixture api) => SignUpAsync(api, "Client", promoteTo: "Admin");

    /// <summary>A fresh SuperAdmin (the seeded one is left alone for the same reason).</summary>
    public static Task<UserSession> SuperAdminAsync(ApiFixture api) => SignUpAsync(api, "Client", promoteTo: "SuperAdmin");

    public static byte[] Png(int width = 800, int height = 600)
    {
        using var bitmap = new SKBitmap(width, height);
        bitmap.Erase(SKColors.HotPink);
        using var image = SKImage.FromBitmap(bitmap);
        using var data = image.Encode(SKEncodedImageFormat.Png, 100);
        return data.ToArray();
    }

    public static MultipartFormDataContent FileForm(byte[] bytes, string fileName = "photo.png", string contentType = "image/png")
    {
        var content = new ByteArrayContent(bytes);
        content.Headers.ContentType = new MediaTypeHeaderValue(contentType);
        return new MultipartFormDataContent { { content, "file", fileName } };
    }

    private static async Task<UserSession> SignUpAsync(ApiFixture api, string role, string? promoteTo, string firstName = "Awa", string lastName = "Koné")
    {
        var client = api.CreateClient();
        var email = $"{role.ToLowerInvariant()}{Guid.NewGuid():N}@example.com";

        (await client.PostAsJsonAsync("/api/v1/auth/register", new RegisterRequest(email, Password, firstName, lastName, role, true))).EnsureSuccessStatusCode();
        var message = api.Emails.Messages.Last(m => m.To == email && m.Subject.Contains("Confirmez", StringComparison.OrdinalIgnoreCase));
        var token = Uri.UnescapeDataString(TokenPattern().Match(message.TextBody).Groups[1].Value);
        (await client.PostAsJsonAsync("/api/v1/auth/confirm-email", new ConfirmEmailRequest(token))).EnsureSuccessStatusCode();

        if (promoteTo is not null)
        {
            await using var scope = api.Services.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<KekeBeautyDbContext>();
            await db.Users.Where(u => u.Email == email).ExecuteUpdateAsync(set => set.SetProperty(u => u.Roles, new List<string> { promoteTo }));
        }

        var login = await client.PostAsJsonAsync("/api/v1/auth/login", new LoginRequest(email, Password));
        login.EnsureSuccessStatusCode();
        var challenge = (await login.Content.ReadFromJsonAsync<ChallengeResponse>())!;

        AuthResponse auth;
        if (challenge.Status == ChallengeStatus.PasswordOnly)
        {
            // A plain client needs no second factor.
            var complete = await client.PostAsJsonAsync("/api/v1/auth/complete-login", new ChallengeRequest(challenge.ChallengeToken));
            complete.EnsureSuccessStatusCode();
            auth = (await complete.Content.ReadFromJsonAsync<AuthResponse>())!;
        }
        else
        {
            var setup = await client.PostAsJsonAsync("/api/v1/auth/2fa/setup", new ChallengeRequest(challenge.ChallengeToken));
            setup.EnsureSuccessStatusCode();
            var secret = (await setup.Content.ReadFromJsonAsync<TwoFactorSetupResponse>())!.Secret;
            var code = new Totp(Base32Encoding.ToBytes(secret)).ComputeTotp(api.Clock.GetUtcNow().UtcDateTime);
            var enable = await client.PostAsJsonAsync("/api/v1/auth/2fa/enable", new ChallengeCodeRequest(challenge.ChallengeToken, code));
            enable.EnsureSuccessStatusCode();
            auth = (await enable.Content.ReadFromJsonAsync<AuthResponse>())!;
        }

        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", auth.AccessToken);
        return new UserSession(client, auth.User.Id, email);
    }
}

/// <summary>A published, bookable establishment created through the real onboarding and agenda endpoints.</summary>
internal sealed record Salon(Guid Id, string Slug, Guid ServiceHour, Guid ServiceHalfHour, UserSession Owner);

internal static class TestSalons
{
    private sealed record Geo(Guid Id, string Name);

    private sealed record Country(Guid Id, string Name);

    private sealed record Category(Guid Id, string Name, string Slug, string Icon);

    /// <summary>
    /// Registers, fills, submits and gets approved a new salon open Monday to Saturday 09:00-18:00 (UTC), with a
    /// one-hour and a half-hour service, then (unless <paramref name="subscribe"/> is false) subscribes to Premium and enables online booking (grid 30 min, no lead time).
    /// </summary>
    public static async Task<Salon> PublishAsync(ApiFixture api, UserSession admin, bool autoConfirm = false, int cancelLimitMinutes = 120, bool subscribe = true)
    {
        var owner = await TestAccounts.PartnerAsync(api);
        using var anonymous = api.CreateClient();
        var country = (await anonymous.GetFromJsonAsync<List<Country>>("/api/v1/geo/countries"))!.Single();
        var region = (await anonymous.GetFromJsonAsync<List<Geo>>($"/api/v1/geo/countries/{country.Id}/regions"))!.Single(r => r.Name.Contains("Abidjan", StringComparison.Ordinal));
        var city = (await anonymous.GetFromJsonAsync<List<Geo>>($"/api/v1/geo/regions/{region.Id}/cities"))!.Single();
        var commune = (await anonymous.GetFromJsonAsync<List<Geo>>($"/api/v1/geo/cities/{city.Id}/communes"))!.Single(c => c.Name == "Cocody");
        var category = (await anonymous.GetFromJsonAsync<List<Category>>("/api/v1/categories"))!.Single(c => c.Slug == "salon-de-coiffure");

        var name = $"Salon Rdv {Guid.NewGuid():N}";
        var create = await owner.Client.PostAsJsonAsync("/api/v1/pro/establishments", new
        {
            name,
            description = "Salon de test pour les rendez-vous en ligne.",
            customerServicePhone = "+22507000011",
            email = (string?)null,
            categoryIds = new[] { category.Id },
            communeId = commune.Id,
            addressLine = "Rue des Tests",
            latitude = 5.36,
            longitude = -3.99,
        });
        create.EnsureSuccessStatusCode();
        var id = (await create.Content.ReadFromJsonAsync<KekeBeauty.Api.Endpoints.CreatedId>())!.Id;
        var baseUrl = $"/api/v1/pro/establishments/{id}";

        var hours = Enumerable.Range(1, 6).Select(day => new { dayOfWeek = day, opensAt = "09:00", closesAt = "18:00" });
        (await owner.Client.PutAsJsonAsync($"{baseUrl}/opening-hours", hours)).EnsureSuccessStatusCode();
        using (var photo = TestAccounts.FileForm(TestAccounts.Png()))
        {
            (await owner.Client.PostAsync($"{baseUrl}/media", photo)).EnsureSuccessStatusCode();
        }

        (await owner.Client.PutAsJsonAsync("/api/v1/pro/kyc", new { holderName = "Awa Koné", documentType = "NationalId", documentNumber = $"CI{Random.Shared.NextInt64(10_000_000, 99_999_999)}", documentExpiry = "2032-05-01" })).EnsureSuccessStatusCode();
        foreach (var side in new[] { "front", "back" })
        {
            using var document = TestAccounts.FileForm(TestAccounts.Png(900, 600));
            (await owner.Client.PutAsync($"/api/v1/pro/kyc/documents/{side}", document)).EnsureSuccessStatusCode();
        }

        (await owner.Client.PostAsync($"{baseUrl}/submit", null)).EnsureSuccessStatusCode();
        var queue = (await admin.Client.GetFromJsonAsync<KekeBeauty.Infrastructure.Partner.QueuePage>("/api/v1/admin/kyc/queue?pageSize=50"))!;
        var review = queue.Items.Single(i => i.EstablishmentName == name);
        (await admin.Client.PostAsJsonAsync($"/api/v1/admin/kyc/{review.ReviewId}/decision", new { decision = "Approve", comment = (string?)null })).EnsureSuccessStatusCode();

        var hour = await AddServiceAsync(owner, baseUrl, category.Id, "Coupe longue", 10000, 60);
        var half = await AddServiceAsync(owner, baseUrl, category.Id, "Brushing", 5000, 30);

        if (subscribe)
        {
            await SubscribePremiumAsync(owner, id);
            var settings = await owner.Client.PutAsJsonAsync($"{baseUrl}/booking-settings", new
            {
                bookingEnabled = true,
                autoConfirm,
                minLeadMinutes = 0,
                maxHorizonDays = 60,
                cancelLimitMinutes,
                slotStepMinutes = 30,
            });
            settings.EnsureSuccessStatusCode();
        }

        var detail = (await owner.Client.GetFromJsonAsync<KekeBeauty.Infrastructure.Partner.PartnerEstablishmentDetail>(baseUrl))!;
        return new Salon(id, detail.Slug, hour, half, owner);
    }

    /// <summary>Subscribes to the Premium plan and pays through the development gateway; returns the checkout.</summary>
    public static async Task<KekeBeauty.Infrastructure.Billing.CheckoutDto> SubscribePremiumAsync(UserSession owner, Guid establishmentId, string cycle = "Monthly", bool pay = true)
    {
        var response = await owner.Client.PostAsJsonAsync($"/api/v1/pro/establishments/{establishmentId}/subscription", new { planCode = "premium", cycle });
        response.IsSuccessStatusCode.Should().BeTrue(await response.Content.ReadAsStringAsync());
        var checkout = (await response.Content.ReadFromJsonAsync<KekeBeauty.Infrastructure.Billing.CheckoutDto>())!;
        if (pay)
        {
            (await owner.Client.PostAsJsonAsync($"/api/v1/dev/payments/{checkout.TransactionId}/simulate", new { status = "accepted" })).EnsureSuccessStatusCode();
        }

        return checkout;
    }

    /// <summary>10:00 UTC on a Tuesday-to-Saturday at least <paramref name="daysAhead"/> days after the fake clock.</summary>
    public static DateTimeOffset NextOpenDay(ApiFixture api, int daysAhead = 2, int hour = 10)
    {
        var day = api.Clock.GetUtcNow().Date.AddDays(daysAhead);
        while (day.DayOfWeek is DayOfWeek.Sunday or DayOfWeek.Monday)
        {
            day = day.AddDays(1);
        }

        return new DateTimeOffset(day.AddHours(hour), TimeSpan.Zero);
    }

    private static async Task<Guid> AddServiceAsync(UserSession owner, string baseUrl, Guid categoryId, string name, decimal price, int minutes)
    {
        var response = await owner.Client.PostAsJsonAsync($"{baseUrl}/services", new
        {
            name,
            description = (string?)null,
            price,
            priceType = "Fixed",
            durationMinutes = minutes,
            categoryId,
            groupId = (Guid?)null,
            isBookable = true,
            isVisible = true,
        });
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<KekeBeauty.Api.Endpoints.CreatedId>())!.Id;
    }
}

internal static class TestCleanup
{
    /// <summary>Removes the establishments of the given owners so that other tests still see exactly the demo data.</summary>
    public static async Task DeleteEstablishmentsAsync(ApiFixture api, params Guid[] ownerIds)
    {
        await using var scope = api.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<KekeBeautyDbContext>();
        var ids = await db.Establishments
            .Where(e => e.OwnerUserId != null && ownerIds.Contains(e.OwnerUserId.Value))
            .Select(e => e.Id)
            .ToListAsync();

        // Nothing may outlive its establishment: leftover appointments would be picked up by later maintenance runs.
        var subscriptionIds = await db.Subscriptions.Where(s => ids.Contains(s.EstablishmentId)).Select(s => s.Id).ToListAsync();
        var invoiceIds = await db.Invoices.Where(i => subscriptionIds.Contains(i.SubscriptionId)).Select(i => i.Id).ToListAsync();
        await db.Payments.Where(p => p.InvoiceId != null && invoiceIds.Contains(p.InvoiceId.Value)).ExecuteDeleteAsync();
        var appointmentPaymentIds = await db.AppointmentPayments.Where(p => ids.Contains(p.EstablishmentId)).Select(p => p.Id).ToListAsync();
        await db.Payments.Where(p => p.AppointmentPaymentId != null && appointmentPaymentIds.Contains(p.AppointmentPaymentId.Value)).ExecuteDeleteAsync();
        await db.AppointmentPayments.Where(p => ids.Contains(p.EstablishmentId)).ExecuteDeleteAsync();
        await db.Payouts.Where(p => ids.Contains(p.EstablishmentId)).ExecuteDeleteAsync();
        await db.Invoices.Where(i => invoiceIds.Contains(i.Id)).ExecuteDeleteAsync();
        await db.Subscriptions.Where(s => subscriptionIds.Contains(s.Id)).ExecuteDeleteAsync();
        await db.WaitlistEntries.Where(w => ids.Contains(w.EstablishmentId)).ExecuteDeleteAsync();
        await db.LoyaltyTransactions.Where(t => ids.Contains(t.EstablishmentId)).ExecuteDeleteAsync();
        await db.LoyaltyPrograms.Where(p => ids.Contains(p.EstablishmentId)).ExecuteDeleteAsync();
        await db.GiftCards.Where(g => ids.Contains(g.EstablishmentId)).ExecuteDeleteAsync();
        await db.GroupBookingRequests.Where(r => ids.Contains(r.EstablishmentId)).ExecuteDeleteAsync();
        await db.Appointments.Where(a => ids.Contains(a.EstablishmentId)).ExecuteDeleteAsync();
        await db.TimeOffs.Where(t => ids.Contains(t.EstablishmentId)).ExecuteDeleteAsync();
        await db.Resources.Where(r => ids.Contains(r.EstablishmentId)).ExecuteDeleteAsync();
        await db.OpeningHours.Where(h => ids.Contains(h.EstablishmentId)).ExecuteDeleteAsync();
        await db.Establishments.Where(e => ids.Contains(e.Id)).ExecuteDeleteAsync();
    }
}
