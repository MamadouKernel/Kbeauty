using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using KekeBeauty.Infrastructure.Partner;
using KekeBeauty.Infrastructure.Search;

namespace KekeBeauty.IntegrationTests;

[Collection(ApiCollection.Name)]
public sealed class AcceptedPaymentsTests(ApiFixture api)
{
    [Fact]
    public async Task A_partner_declares_how_clients_pay_and_visitors_see_it()
    {
        using var admin = await TestAccounts.AdminAsync(api);
        var salon = await TestSalons.PublishAsync(api, admin, subscribe: false);
        var url = $"/api/v1/pro/establishments/{salon.Id}/accepted-payments";

        try
        {
            // New establishments take cash by default.
            var initial = (await salon.Owner.Client.GetFromJsonAsync<AcceptedPaymentsDto>(url))!;
            initial.Cash.Should().BeTrue();
            initial.MobileMoney.Should().BeEmpty();

            var saved = await salon.Owner.Client.PutAsJsonAsync(url, new AcceptedPaymentsInput(true, [
                new MobileMoneyInput("wave-cote-divoire", "07 00 00 12 34", "Awa Koné"),
                new MobileMoneyInput("orange-money-cote-divoire", "05 11 22 33 44", "Awa Koné"),
            ]));
            saved.StatusCode.Should().Be(HttpStatusCode.OK);
            (await saved.Content.ReadFromJsonAsync<AcceptedPaymentsDto>())!.MobileMoney.Should().HaveCount(2)
                .And.Contain(m => m.Operator == "wave-cote-divoire" && m.Account == "+2250700001234");

            using var visitor = api.CreateClient();
            var page = (await visitor.GetFromJsonAsync<EstablishmentDetails>($"/api/v1/establishments/{salon.Slug}"))!;
            page.AcceptedPayments.Cash.Should().BeTrue();
            page.AcceptedPayments.MobileMoney.Select(m => m.Operator).Should().BeEquivalentTo("wave-cote-divoire", "orange-money-cote-divoire");
        }
        finally
        {
            await TestCleanup.DeleteEstablishmentsAsync(api, salon.Owner.UserId);
        }
    }

    [Fact]
    public async Task Invalid_choices_and_other_partners_are_refused()
    {
        using var admin = await TestAccounts.AdminAsync(api);
        var salon = await TestSalons.PublishAsync(api, admin, subscribe: false);
        using var intruder = await TestAccounts.PartnerAsync(api);
        var url = $"/api/v1/pro/establishments/{salon.Id}/accepted-payments";

        try
        {
            (await salon.Owner.Client.PutAsJsonAsync(url, new AcceptedPaymentsInput(false, []))).StatusCode.Should().Be(HttpStatusCode.BadRequest);
            (await salon.Owner.Client.PutAsJsonAsync(url, new AcceptedPaymentsInput(true, [new MobileMoneyInput("bitcoin", "07 00 00 12 34", "Awa")]))).StatusCode.Should().Be(HttpStatusCode.BadRequest);
            (await salon.Owner.Client.PutAsJsonAsync(url, new AcceptedPaymentsInput(true, [new MobileMoneyInput("wave-cote-divoire", "pas un numéro", "Awa")]))).StatusCode.Should().Be(HttpStatusCode.BadRequest);
            (await salon.Owner.Client.PutAsJsonAsync(url, new AcceptedPaymentsInput(true, [
                new MobileMoneyInput("wave-cote-divoire", "07 00 00 12 34", "Awa"),
                new MobileMoneyInput("wave-cote-divoire", "07 00 00 12 35", "Awa"),
            ]))).StatusCode.Should().Be(HttpStatusCode.BadRequest);

            (await intruder.Client.GetAsync(url)).StatusCode.Should().Be(HttpStatusCode.NotFound);
            (await intruder.Client.PutAsJsonAsync(url, new AcceptedPaymentsInput(true, []))).StatusCode.Should().Be(HttpStatusCode.NotFound);
        }
        finally
        {
            await TestCleanup.DeleteEstablishmentsAsync(api, salon.Owner.UserId);
        }
    }
}
