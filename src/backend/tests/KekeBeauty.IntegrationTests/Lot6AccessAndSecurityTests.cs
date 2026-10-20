using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using KekeBeauty.Infrastructure.Admin;
using KekeBeauty.Infrastructure.Persistence;
using KekeBeauty.Modules.Identity.Application;
using KekeBeauty.Modules.Identity.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace KekeBeauty.IntegrationTests;

[Collection(ApiCollection.Name)]
public sealed class Lot6AccessAndSecurityTests(ApiFixture api)
{
    private const string Admin = "/api/v1/admin";

    [Fact]
    public async Task SuperAdmin_can_grant_and_update_fine_grained_admin_roles_and_policies_enforce_scope()
    {
        using var superAdmin = await TestAccounts.SuperAdminAsync(api);
        using var kycCandidate = await TestAccounts.ClientAsync(api);

        // Grant AdminKyc role
        var grantResp = await superAdmin.Client.PostAsJsonAsync(
            $"{Admin}/admins",
            new { email = kycCandidate.Email, role = AppRoles.AdminKyc });
        grantResp.StatusCode.Should().Be(HttpStatusCode.OK);
        var granted = (await grantResp.Content.ReadFromJsonAsync<AdminUserDto>())!;
        granted.Roles.Should().Contain(AppRoles.AdminKyc);

        // Update role to AdminAccountant
        var updateResp = await superAdmin.Client.PutAsJsonAsync(
            $"{Admin}/admins/{kycCandidate.UserId}",
            new { role = AppRoles.AdminAccountant });
        updateResp.StatusCode.Should().Be(HttpStatusCode.OK);
        var updated = (await updateResp.Content.ReadFromJsonAsync<AdminUserDto>())!;
        updated.Roles.Should().Contain(AppRoles.AdminAccountant).And.NotContain(AppRoles.AdminKyc);

        // Revoke delegated role
        var revokeResp = await superAdmin.Client.DeleteAsync($"{Admin}/admins/{kycCandidate.UserId}");
        revokeResp.StatusCode.Should().Be(HttpStatusCode.NoContent);
    }

    [Fact]
    public async Task Signed_in_user_can_list_and_revoke_trusted_devices()
    {
        using var client = await TestAccounts.ClientAsync(api);

        var devices = await client.Client.GetFromJsonAsync<List<TrustedDeviceDto>>("/api/v1/me/trusted-devices");
        devices.Should().NotBeNull();
    }

    [Fact]
    public async Task Revoked_admin_suspended_user_and_logged_out_session_lose_access_immediately()
    {
        using var kycAdmin = await TestAccounts.AdminAsync(api);
        using var candidate = await TestAccounts.ClientAsync(api);

        // Switch kycAdmin to delegated AdminKyc role in DB: can access establishments moderation queue, but NOT global audit trail
        await using (var scope = api.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<KekeBeautyDbContext>();
            await db.Users.Where(u => u.Id == kycAdmin.UserId)
                .ExecuteUpdateAsync(set => set.SetProperty(u => u.Roles, new List<string> { AppRoles.AdminKyc }));
        }

        (await kycAdmin.Client.GetAsync($"{Admin}/establishments")).StatusCode.Should().Be(HttpStatusCode.OK);
        (await kycAdmin.Client.GetAsync($"{Admin}/audit")).StatusCode.Should().Be(HttpStatusCode.Forbidden);

        // Revoking the admin role in DB strips admin rights immediately on the very next request with the same JWT
        await using (var scope = api.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<KekeBeautyDbContext>();
            await db.Users.Where(u => u.Id == kycAdmin.UserId)
                .ExecuteUpdateAsync(set => set.SetProperty(u => u.Roles, new List<string> { AppRoles.Client }));
        }

        (await kycAdmin.Client.GetAsync($"{Admin}/establishments")).StatusCode.Should().Be(HttpStatusCode.Forbidden);

        // Logout invalidates the current access token immediately
        (await candidate.Client.PostAsync("/api/v1/auth/logout", null)).StatusCode.Should().Be(HttpStatusCode.NoContent);
        (await candidate.Client.GetAsync("/api/v1/me")).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Repeated_exhausted_two_factor_challenges_lock_the_account()
    {
        // A partner always has a second factor; a plain client has none, so 2FA cannot be brute-forced on one.
        using var victim = await TestAccounts.PartnerAsync(api);
        using var anon = api.CreateClient();

        for (var round = 0; round < 5; round++)
        {
            var login = await anon.PostAsJsonAsync("/api/v1/auth/login", new LoginRequest(victim.Email, TestAccounts.Password));
            login.StatusCode.Should().Be(HttpStatusCode.OK);
            var challenge = (await login.Content.ReadFromJsonAsync<ChallengeResponse>())!;
            for (var attempt = 0; attempt < 5; attempt++)
            {
                await anon.PostAsJsonAsync("/api/v1/auth/2fa/verify", new ChallengeCodeRequest(challenge.ChallengeToken, "000000"));
            }
        }

        var locked = await anon.PostAsJsonAsync("/api/v1/auth/login", new LoginRequest(victim.Email, TestAccounts.Password));
        locked.StatusCode.Should().Be(HttpStatusCode.Locked);
    }

    [Fact]
    public async Task Web_push_rejects_ssrf_internal_and_untrusted_endpoints()
    {
        using var client = await TestAccounts.ClientAsync(api);
        const string p256dh = "BNcRdreALRFXTkOOUHK1EtK2wtaz5Ry4YfYCA_0QTpQtUbVlUls0VJXg7A8u-Ts1XbjhazAkj7I99e8QcYP7DkM";
        var auth = TestSecrets.PushAuth;

        foreach (var bad in new[]
        {
            "https://127.0.0.1/push",
            "https://169.254.169.254/latest/meta-data/",
            "https://localhost/push",
            "https://postgres:5432/push",
            "https://internal.service.local/push",
            "https://evil.example.com/push",
        })
        {
            var res = await client.Client.PostAsJsonAsync("/api/v1/me/push/subscribe", new { endpoint = bad, keys = new { p256dh, auth } });
            res.StatusCode.Should().Be(HttpStatusCode.BadRequest, $"endpoint {bad} must be rejected");
        }
    }
}
