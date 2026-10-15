using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using KekeBeauty.Api.Endpoints;
using KekeBeauty.Infrastructure.Partner;
using KekeBeauty.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace KekeBeauty.IntegrationTests;

[Collection(ApiCollection.Name)]
public sealed class PartnerCatalogTests(ApiFixture api)
{
    private sealed record CategoryItem(Guid Id, string Name, string Slug, string Icon);

    private sealed record GeoItem(Guid Id, string Name);

    private sealed record CountryItem(Guid Id, string Name);

    private async Task<(Guid Id, Guid CategoryId)> CreateEstablishmentAsync(UserSession partner)
    {
        using var anonymous = api.CreateClient();
        var categories = (await anonymous.GetFromJsonAsync<List<CategoryItem>>("/api/v1/categories"))!;
        var salon = categories.Single(c => c.Slug == "salon-de-coiffure").Id;
        var country = (await anonymous.GetFromJsonAsync<List<CountryItem>>("/api/v1/geo/countries"))!.Single();
        var region = (await anonymous.GetFromJsonAsync<List<GeoItem>>($"/api/v1/geo/countries/{country.Id}/regions"))!.Single(r => r.Name.Contains("Abidjan", StringComparison.Ordinal));
        var city = (await anonymous.GetFromJsonAsync<List<GeoItem>>($"/api/v1/geo/regions/{region.Id}/cities"))!.Single();
        var commune = (await anonymous.GetFromJsonAsync<List<GeoItem>>($"/api/v1/geo/cities/{city.Id}/communes"))!.First();

        var input = new EstablishmentInput(
            $"Catalogue Test {Guid.NewGuid():N}", "Salon de test pour la gestion des prestations.", "+22507000000", null,
            [salon], commune.Id, "Rue du Test", 5.35, -4.0);
        var response = await partner.Client.PostAsJsonAsync("/api/v1/pro/establishments", input);
        response.StatusCode.Should().Be(HttpStatusCode.Created);
        return ((await response.Content.ReadFromJsonAsync<CreatedId>())!.Id, salon);
    }

    private static ServiceInput Service(Guid categoryId, string name = "Box braids", Guid? groupId = null) =>
        new(name, "Tresses longues", 25000, "From", 240, categoryId, groupId, true, true);

    private async Task CleanupAsync(Guid ownerId)
    {
        await using var scope = api.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<KekeBeautyDbContext>();
        await db.Establishments.Where(e => e.OwnerUserId == ownerId).ExecuteDeleteAsync();
    }

    [Fact]
    public async Task Services_and_groups_can_be_created_edited_ordered_and_removed()
    {
        using var partner = await TestAccounts.PartnerAsync(api);

        try
        {
            var (id, category) = await CreateEstablishmentAsync(partner);
            var baseUrl = $"/api/v1/pro/establishments/{id}";

            var groupResponse = await partner.Client.PostAsJsonAsync($"{baseUrl}/service-groups", new GroupInput("Tresses"));
            groupResponse.StatusCode.Should().Be(HttpStatusCode.Created);
            var groupId = (await groupResponse.Content.ReadFromJsonAsync<CreatedId>())!.Id;

            var first = (await (await partner.Client.PostAsJsonAsync($"{baseUrl}/services", Service(category, "Box braids", groupId))).Content.ReadFromJsonAsync<CreatedId>())!.Id;
            var second = (await (await partner.Client.PostAsJsonAsync($"{baseUrl}/services", Service(category, "Cornrows"))).Content.ReadFromJsonAsync<CreatedId>())!.Id;

            var detail = (await partner.Client.GetFromJsonAsync<PartnerEstablishmentDetail>(baseUrl))!;
            detail.Services.Select(s => s.Name).Should().Equal("Box braids", "Cornrows");
            detail.Services[0].GroupId.Should().Be(groupId);
            detail.Services[0].PriceType.Should().Be("From");
            detail.Groups.Should().ContainSingle(g => g.Name == "Tresses");

            (await partner.Client.PutAsJsonAsync($"{baseUrl}/services/order", new OrderInput([second, first]))).StatusCode.Should().Be(HttpStatusCode.NoContent);
            (await partner.Client.PutAsJsonAsync($"{baseUrl}/services/{first}", Service(category, "Box braids XL", groupId) with { Price = 30000, PriceType = "Fixed" }))
                .StatusCode.Should().Be(HttpStatusCode.NoContent);
            detail = (await partner.Client.GetFromJsonAsync<PartnerEstablishmentDetail>(baseUrl))!;
            detail.Services.Select(s => s.Name).Should().Equal("Cornrows", "Box braids XL");
            detail.Services[1].Price.Should().Be(30000);
            detail.Services[1].PriceType.Should().Be("Fixed");

            // Deleting a group keeps its services, ungrouped.
            (await partner.Client.DeleteAsync($"{baseUrl}/service-groups/{groupId}")).StatusCode.Should().Be(HttpStatusCode.NoContent);
            detail = (await partner.Client.GetFromJsonAsync<PartnerEstablishmentDetail>(baseUrl))!;
            detail.Groups.Should().BeEmpty();
            detail.Services.Should().OnlyContain(s => s.GroupId == null);

            // Deletion is logical: the row stays, but it disappears from the listing and cannot be edited any more.
            (await partner.Client.DeleteAsync($"{baseUrl}/services/{second}")).StatusCode.Should().Be(HttpStatusCode.NoContent);
            (await partner.Client.DeleteAsync($"{baseUrl}/services/{second}")).StatusCode.Should().Be(HttpStatusCode.NotFound);
            (await partner.Client.GetFromJsonAsync<PartnerEstablishmentDetail>(baseUrl))!.Services.Should().ContainSingle();
            await using var scope = api.Services.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<KekeBeautyDbContext>();
            (await db.Services.SingleAsync(s => s.Id == second)).DeletedAt.Should().NotBeNull();
        }
        finally
        {
            await CleanupAsync(partner.UserId);
        }
    }

    [Fact]
    public async Task Service_input_is_validated()
    {
        using var partner = await TestAccounts.PartnerAsync(api);

        try
        {
            var (id, category) = await CreateEstablishmentAsync(partner);
            var url = $"/api/v1/pro/establishments/{id}/services";
            var valid = Service(category);

            (await partner.Client.PostAsJsonAsync(url, valid with { Name = "" })).StatusCode.Should().Be(HttpStatusCode.BadRequest);
            (await partner.Client.PostAsJsonAsync(url, valid with { Price = -1 })).StatusCode.Should().Be(HttpStatusCode.BadRequest);
            (await partner.Client.PostAsJsonAsync(url, valid with { DurationMinutes = 0 })).StatusCode.Should().Be(HttpStatusCode.BadRequest);
            (await partner.Client.PostAsJsonAsync(url, valid with { PriceType = "Free" })).StatusCode.Should().Be(HttpStatusCode.BadRequest);
            (await partner.Client.PostAsJsonAsync(url, valid with { CategoryId = Guid.NewGuid() })).StatusCode.Should().Be(HttpStatusCode.BadRequest);
            (await partner.Client.PostAsJsonAsync(url, valid with { GroupId = Guid.NewGuid() })).StatusCode.Should().Be(HttpStatusCode.BadRequest);
            (await partner.Client.PostAsJsonAsync($"/api/v1/pro/establishments/{id}/service-groups", new GroupInput("x"))).StatusCode.Should().Be(HttpStatusCode.BadRequest);
            (await partner.Client.PutAsJsonAsync($"{url}/order", new OrderInput([Guid.NewGuid()]))).StatusCode.Should().Be(HttpStatusCode.BadRequest);
            (await partner.Client.GetFromJsonAsync<PartnerEstablishmentDetail>($"/api/v1/pro/establishments/{id}"))!.Services.Should().BeEmpty();
        }
        finally
        {
            await CleanupAsync(partner.UserId);
        }
    }

    [Fact]
    public async Task Another_partner_cannot_touch_the_catalog()
    {
        using var owner = await TestAccounts.PartnerAsync(api);
        using var intruder = await TestAccounts.PartnerAsync(api, "Moussa", "Traoré");

        try
        {
            var (id, category) = await CreateEstablishmentAsync(owner);
            var url = $"/api/v1/pro/establishments/{id}";

            (await intruder.Client.PostAsJsonAsync($"{url}/services", Service(category))).StatusCode.Should().Be(HttpStatusCode.NotFound);
            (await intruder.Client.PostAsJsonAsync($"{url}/service-groups", new GroupInput("Pirate"))).StatusCode.Should().Be(HttpStatusCode.NotFound);
            using var photo = TestAccounts.FileForm(TestAccounts.Png());
            (await intruder.Client.PostAsync($"{url}/media", photo)).StatusCode.Should().Be(HttpStatusCode.NotFound);
        }
        finally
        {
            await CleanupAsync(owner.UserId);
        }
    }
}
