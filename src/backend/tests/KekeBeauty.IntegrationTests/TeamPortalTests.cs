using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using KekeBeauty.Infrastructure.Persistence;
using KekeBeauty.Infrastructure.Scheduling;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace KekeBeauty.IntegrationTests;

[Collection(ApiCollection.Name)]
public sealed class TeamPortalTests(ApiFixture api)
{
    [Fact]
    public async Task The_owner_links_a_collaborator_who_then_sees_her_own_portal_only()
    {
        using var admin = await TestAccounts.AdminAsync(api);
        var salon = await TestSalons.PublishAsync(api, admin, autoConfirm: true);
        using var staff = await TestAccounts.StaffAsync(api);
        using var stranger = await TestAccounts.PartnerAsync(api);
        using var client = await TestAccounts.ClientAsync(api);

        try
        {
            var team = $"/api/v1/pro/establishments/{salon.Id}/team";
            var members = (await salon.Owner.Client.GetFromJsonAsync<List<TeamMemberDto>>(team))!;
            var resource = members.Should().ContainSingle().Subject;
            resource.UserId.Should().BeNull();

            // Not the owner's establishment, bad rate, unknown e-mail, owner as collaborator.
            (await stranger.Client.GetAsync(team)).StatusCode.Should().Be(HttpStatusCode.NotFound);
            (await salon.Owner.Client.PutAsJsonAsync($"{team}/{resource.ResourceId}", new TeamAssignmentInput(staff.Email, 120))).StatusCode.Should().Be(HttpStatusCode.BadRequest);
            (await salon.Owner.Client.PutAsJsonAsync($"{team}/{resource.ResourceId}", new TeamAssignmentInput("nobody@example.com", 30))).StatusCode.Should().Be(HttpStatusCode.BadRequest);
            (await salon.Owner.Client.PutAsJsonAsync($"{team}/{resource.ResourceId}", new TeamAssignmentInput(salon.Owner.Email, 30))).StatusCode.Should().Be(HttpStatusCode.BadRequest);

            // The portal is closed to everybody else, including the owner and plain clients.
            (await client.Client.GetAsync("/api/v1/team/me")).StatusCode.Should().Be(HttpStatusCode.Forbidden);
            (await salon.Owner.Client.GetAsync("/api/v1/team/me")).StatusCode.Should().Be(HttpStatusCode.Forbidden);

            var assigned = await salon.Owner.Client.PutAsJsonAsync($"{team}/{resource.ResourceId}", new TeamAssignmentInput(staff.Email, 35));
            assigned.StatusCode.Should().Be(HttpStatusCode.OK);
            var linked = (await assigned.Content.ReadFromJsonAsync<TeamMemberDto>())!;
            linked.UserId.Should().Be(staff.UserId);
            linked.CommissionPercent.Should().Be(35);

            var memberships = (await staff.Client.GetFromJsonAsync<List<TeamMembershipDto>>("/api/v1/team/me"))!;
            memberships.Should().ContainSingle(m => m.EstablishmentId == salon.Id && m.CommissionPercent == 35);

            var planning = await staff.Client.GetAsync("/api/v1/team/planning");
            planning.StatusCode.Should().Be(HttpStatusCode.OK);
            (await planning.Content.ReadFromJsonAsync<TeamPlanningDto>())!.Appointments.Should().BeEmpty();

            var commissions = (await staff.Client.GetFromJsonAsync<List<CommissionSummaryDto>>("/api/v1/team/commissions"))!;
            commissions.Should().ContainSingle(c => c.Services == 0 && c.Total == 0);
            (await staff.Client.GetAsync("/api/v1/team/commissions?from=2026-10-10&to=2026-01-01")).StatusCode.Should().Be(HttpStatusCode.BadRequest);

            // The owner follows the same figures; another owner cannot.
            (await salon.Owner.Client.GetAsync($"/api/v1/pro/establishments/{salon.Id}/commissions")).StatusCode.Should().Be(HttpStatusCode.OK);
            (await stranger.Client.GetAsync($"/api/v1/pro/establishments/{salon.Id}/commissions")).StatusCode.Should().Be(HttpStatusCode.NotFound);

            // Starting an appointment that is not hers is a 404, never a leak.
            (await staff.Client.PostAsync($"/api/v1/team/appointments/{Guid.NewGuid()}/start", null)).StatusCode.Should().Be(HttpStatusCode.NotFound);

            (await salon.Owner.Client.DeleteAsync($"{team}/{resource.ResourceId}")).StatusCode.Should().Be(HttpStatusCode.NoContent);
            (await staff.Client.GetFromJsonAsync<List<TeamMembershipDto>>("/api/v1/team/me"))!.Should().BeEmpty();
        }
        finally
        {
            await TestCleanup.DeleteEstablishmentsAsync(api, salon.Owner.UserId);
        }
    }

    [Fact]
    public async Task Linking_a_plain_client_gives_her_the_staff_role_and_unlinking_takes_it_back()
    {
        using var admin = await TestAccounts.AdminAsync(api);
        var salon = await TestSalons.PublishAsync(api, admin, autoConfirm: true);
        using var candidate = await TestAccounts.ClientAsync(api);

        try
        {
            var team = $"/api/v1/pro/establishments/{salon.Id}/team";
            var resource = (await salon.Owner.Client.GetFromJsonAsync<List<TeamMemberDto>>(team))!.Single();

            (await salon.Owner.Client.PutAsJsonAsync($"{team}/{resource.ResourceId}", new TeamAssignmentInput(candidate.Email.ToUpperInvariant(), 20)))
                .StatusCode.Should().Be(HttpStatusCode.OK);
            (await RolesOfAsync(candidate.UserId)).Should().Contain(["Client", "PartnerStaff"]);

            (await salon.Owner.Client.DeleteAsync($"{team}/{resource.ResourceId}")).StatusCode.Should().Be(HttpStatusCode.NoContent);
            (await RolesOfAsync(candidate.UserId)).Should().Equal("Client");
        }
        finally
        {
            await TestCleanup.DeleteEstablishmentsAsync(api, salon.Owner.UserId);
        }
    }

    [Fact]
    public async Task A_tip_is_refused_before_the_treatment_is_finished()
    {
        using var admin = await TestAccounts.AdminAsync(api);
        var salon = await TestSalons.PublishAsync(api, admin, autoConfirm: true);
        using var client = await TestAccounts.ClientAsync(api);

        try
        {
            var booked = await client.Client.PostAsJsonAsync("/api/v1/appointments",
                new BookingRequest(salon.Id, [salon.ServiceHour], null, TestSalons.NextOpenDay(api), "07 00 00 12 34", null));
            var appointment = (await booked.Content.ReadFromJsonAsync<AppointmentDto>())!;
            appointment.CanTip.Should().BeFalse();

            var tip = $"/api/v1/appointments/{appointment.Id}/tip";
            (await client.Client.PostAsJsonAsync(tip, new TipInput(50))).StatusCode.Should().Be(HttpStatusCode.BadRequest);
            (await client.Client.PostAsJsonAsync(tip, new TipInput(500))).StatusCode.Should().Be(HttpStatusCode.Conflict);
        }
        finally
        {
            await TestCleanup.DeleteEstablishmentsAsync(api, salon.Owner.UserId);
        }
    }

    private async Task<List<string>> RolesOfAsync(Guid userId)
    {
        await using var scope = api.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<KekeBeautyDbContext>();
        return await db.Users.AsNoTracking().Where(u => u.Id == userId).Select(u => u.Roles).SingleAsync();
    }
}
