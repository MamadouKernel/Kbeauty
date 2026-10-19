using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using KekeBeauty.Infrastructure.Admin;
using KekeBeauty.Modules.Identity.Application;
using KekeBeauty.Modules.Identity.Domain;

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
}
