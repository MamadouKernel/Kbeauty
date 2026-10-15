using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using KekeBeauty.Infrastructure.Partner;
using KekeBeauty.Infrastructure.Scheduling;

namespace KekeBeauty.IntegrationTests;

[Collection(ApiCollection.Name)]
public sealed class MarkPaidTests(ApiFixture api)
{
    [Fact]
    public async Task The_establishment_records_cash_or_mobile_money_payments_and_the_client_sees_them()
    {
        using var admin = await TestAccounts.AdminAsync(api);
        var salon = await TestSalons.PublishAsync(api, admin, autoConfirm: true);
        using var client = await TestAccounts.ClientAsync(api);
        using var stranger = await TestAccounts.PartnerAsync(api);

        try
        {
            (await salon.Owner.Client.PutAsJsonAsync($"/api/v1/pro/establishments/{salon.Id}/accepted-payments",
                new AcceptedPaymentsInput(true, [new MobileMoneyInput("wave-cote-divoire", "07 00 00 12 34", "Awa Koné")]))).EnsureSuccessStatusCode();

            var booked = await client.Client.PostAsJsonAsync("/api/v1/appointments",
                new BookingRequest(salon.Id, [salon.ServiceHour], null, TestSalons.NextOpenDay(api), "07 00 00 12 34", null));
            booked.StatusCode.Should().Be(HttpStatusCode.Created);
            var appointment = (await booked.Content.ReadFromJsonAsync<AppointmentDto>())!;
            var url = $"/api/v1/pro/appointments/{appointment.Id}";

            // Refused: mobile money without a reference, a method the establishment does not accept, and somebody else.
            (await salon.Owner.Client.PostAsJsonAsync($"{url}/mark-paid", new MarkPaidInput("wave-cote-divoire", null))).StatusCode.Should().Be(HttpStatusCode.BadRequest);
            (await salon.Owner.Client.PostAsJsonAsync($"{url}/mark-paid", new MarkPaidInput("mtn-cote-divoire", "TX12345"))).StatusCode.Should().Be(HttpStatusCode.BadRequest);
            (await stranger.Client.PostAsJsonAsync($"{url}/mark-paid", new MarkPaidInput("Cash", null))).StatusCode.Should().Be(HttpStatusCode.NotFound);
            (await client.Client.PostAsJsonAsync($"{url}/mark-paid", new MarkPaidInput("Cash", null))).StatusCode.Should().Be(HttpStatusCode.Forbidden);

            var paid = await salon.Owner.Client.PostAsJsonAsync($"{url}/mark-paid", new MarkPaidInput("wave-cote-divoire", "WV-2026-0001"));
            paid.StatusCode.Should().Be(HttpStatusCode.OK);
            (await paid.Content.ReadFromJsonAsync<PartnerAppointmentDto>())!.ServicePayment.Should()
                .BeEquivalentTo(new { Method = "wave-cote-divoire", Reference = "WV-2026-0001" }, o => o.ExcludingMissingMembers());

            var mine = (await client.Client.GetFromJsonAsync<List<AppointmentDto>>("/api/v1/me/appointments"))!.Single(a => a.Id == appointment.Id);
            mine.ServicePayment!.Reference.Should().Be("WV-2026-0001");

            (await salon.Owner.Client.PostAsync($"{url}/mark-unpaid", null)).StatusCode.Should().Be(HttpStatusCode.OK);
            var cash = await salon.Owner.Client.PostAsJsonAsync($"{url}/mark-paid", new MarkPaidInput("Cash", null));
            (await cash.Content.ReadFromJsonAsync<PartnerAppointmentDto>())!.ServicePayment!.Method.Should().Be("Cash");
        }
        finally
        {
            await TestCleanup.DeleteEstablishmentsAsync(api, salon.Owner.UserId);
        }
    }
}
