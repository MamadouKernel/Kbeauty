using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using KekeBeauty.Infrastructure.Partner;
using KekeBeauty.Infrastructure.Scheduling;

namespace KekeBeauty.IntegrationTests;

[Collection(ApiCollection.Name)]
public sealed class CounterAndPassTests(ApiFixture api)
{
    [Fact]
    public async Task A_walk_in_is_recorded_at_the_counter_and_blocks_the_resource_for_its_duration()
    {
        using var admin = await TestAccounts.AdminAsync(api);
        var salon = await TestSalons.PublishAsync(api, admin, autoConfirm: true);
        using var stranger = await TestAccounts.PartnerAsync(api);

        try
        {
            var url = $"/api/v1/pro/establishments/{salon.Id}/counter";
            var request = new CounterRequest([salon.ServiceHour], null, null, "Mariam Traoré", "07 00 00 55 66", "Cash", null);

            (await stranger.Client.PostAsJsonAsync(url, request)).StatusCode.Should().Be(HttpStatusCode.NotFound);
            (await salon.Owner.Client.PostAsJsonAsync(url, request with { ClientName = " " })).StatusCode.Should().Be(HttpStatusCode.BadRequest);
            (await salon.Owner.Client.PostAsJsonAsync(url, request with { Phone = "abc" })).StatusCode.Should().Be(HttpStatusCode.BadRequest);
            (await salon.Owner.Client.PostAsJsonAsync(url, request with { StartsAt = api.Clock.GetUtcNow().AddDays(3) })).StatusCode.Should().Be(HttpStatusCode.BadRequest);

            var created = await salon.Owner.Client.PostAsJsonAsync(url, request);
            created.StatusCode.Should().Be(HttpStatusCode.Created);
            var walkIn = (await created.Content.ReadFromJsonAsync<PartnerAppointmentDto>())!;
            walkIn.ClientName.Should().Be("Mariam Traoré");
            walkIn.Status.Should().Be("Confirmed");
            walkIn.ServicePayment.Should().NotBeNull("the walk-in paid in cash");

            // The single resource is now busy: a second walk-in right away is refused, not double-booked.
            (await salon.Owner.Client.PostAsJsonAsync(url, request with { ClientName = "Autre Cliente", PaidMethod = null }))
                .StatusCode.Should().Be(HttpStatusCode.Conflict);

            var listed = await salon.Owner.Client.GetFromJsonAsync<List<PartnerAppointmentDto>>(
                $"/api/v1/pro/establishments/{salon.Id}/appointments");
            listed!.Should().Contain(a => a.Id == walkIn.Id && a.ClientName == "Mariam Traoré");
        }
        finally
        {
            await TestCleanup.DeleteEstablishmentsAsync(api, salon.Owner.UserId);
        }
    }

    [Fact]
    public async Task A_client_pass_is_verified_only_by_the_salon_it_belongs_to()
    {
        using var admin = await TestAccounts.AdminAsync(api);
        var salon = await TestSalons.PublishAsync(api, admin, autoConfirm: true);
        var other = await TestSalons.PublishAsync(api, admin, autoConfirm: true);
        using var client = await TestAccounts.ClientAsync(api);
        using var stranger = await TestAccounts.ClientAsync(api);

        try
        {
            var booked = await client.Client.PostAsJsonAsync("/api/v1/appointments",
                new BookingRequest(salon.Id, [salon.ServiceHour], null, TestSalons.NextOpenDay(api), "07 00 00 12 34", null));
            var appointment = (await booked.Content.ReadFromJsonAsync<AppointmentDto>())!;

            (await stranger.Client.GetAsync($"/api/v1/appointments/{appointment.Id}/pass")).StatusCode.Should().Be(HttpStatusCode.NotFound);
            var issued = await client.Client.GetAsync($"/api/v1/appointments/{appointment.Id}/pass");
            issued.StatusCode.Should().Be(HttpStatusCode.OK);
            var pass = (await issued.Content.ReadFromJsonAsync<PassDto>())!;

            var verify = $"/api/v1/pro/establishments/{salon.Id}/pass/verify";
            var ok = await salon.Owner.Client.PostAsJsonAsync(verify, new PassVerifyInput(pass.Token));
            ok.StatusCode.Should().Be(HttpStatusCode.OK);
            (await ok.Content.ReadFromJsonAsync<PassCheckDto>())!.AppointmentId.Should().Be(appointment.Id);

            // A pasted link works as well as the bare token.
            (await salon.Owner.Client.PostAsJsonAsync(verify, new PassVerifyInput($"https://x.test/pass?token={Uri.EscapeDataString(pass.Token)}")))
                .StatusCode.Should().Be(HttpStatusCode.OK);

            (await salon.Owner.Client.PostAsJsonAsync(verify, new PassVerifyInput(pass.Token + "x"))).StatusCode.Should().Be(HttpStatusCode.BadRequest);
            (await other.Owner.Client.PostAsJsonAsync($"/api/v1/pro/establishments/{other.Id}/pass/verify", new PassVerifyInput(pass.Token)))
                .StatusCode.Should().Be(HttpStatusCode.Forbidden);
        }
        finally
        {
            await TestCleanup.DeleteEstablishmentsAsync(api, salon.Owner.UserId);
            await TestCleanup.DeleteEstablishmentsAsync(api, other.Owner.UserId);
        }
    }
}
