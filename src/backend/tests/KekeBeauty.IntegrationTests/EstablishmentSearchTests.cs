using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using KekeBeauty.Infrastructure.Persistence;
using KekeBeauty.Infrastructure.Search;
using KekeBeauty.Modules.Catalog.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using NetTopologySuite.Geometries;

namespace KekeBeauty.IntegrationTests;

[Collection(ApiCollection.Name)]
public sealed class EstablishmentSearchTests(ApiFixture api)
{
    private sealed record GeoItem(Guid Id, string Name);

    private sealed record CountryItem(Guid Id, string Name);

    // Plateau (Abidjan): the "Onglerie Perle d'Ivoire" demo establishment sits exactly here.
    private const string Plateau = "lat=5.3235&lng=-4.0197";

    private HttpClient Client() => api.CreateClient();

    private static async Task<EstablishmentPage> Search(HttpClient client, string query = "") =>
        (await client.GetFromJsonAsync<EstablishmentPage>($"/api/v1/establishments?{query}"))!;

    private static async Task<IReadOnlyList<GeoItem>> Communes(HttpClient client)
    {
        var country = (await client.GetFromJsonAsync<List<CountryItem>>("/api/v1/geo/countries"))!.Single();
        var regions = (await client.GetFromJsonAsync<List<GeoItem>>($"/api/v1/geo/countries/{country.Id}/regions"))!;
        var abidjan = regions.Single(r => r.Name.Contains("Abidjan", StringComparison.Ordinal));
        var city = (await client.GetFromJsonAsync<List<GeoItem>>($"/api/v1/geo/regions/{abidjan.Id}/cities"))!.Single();
        return (await client.GetFromJsonAsync<List<GeoItem>>($"/api/v1/geo/cities/{city.Id}/communes"))!;
    }

    private static async Task<Guid> AbidjanCityId(HttpClient client)
    {
        var country = (await client.GetFromJsonAsync<List<CountryItem>>("/api/v1/geo/countries"))!.Single();
        var regions = (await client.GetFromJsonAsync<List<GeoItem>>($"/api/v1/geo/countries/{country.Id}/regions"))!;
        var region = regions.Single(r => r.Name.Contains("Abidjan", StringComparison.Ordinal));
        return (await client.GetFromJsonAsync<List<GeoItem>>($"/api/v1/geo/regions/{region.Id}/cities"))!.Single().Id;
    }

    [Fact]
    public async Task Lists_the_active_demo_establishments_featured_first()
    {
        using var client = Client();

        var page = await Search(client, "pageSize=50");

        page.Total.Should().Be(10);
        page.Items.Take(3).Should().OnlyContain(i => i.IsFeatured);
        page.Items.Skip(3).Should().OnlyContain(i => !i.IsFeatured);
        page.Items.Should().OnlyContain(i => i.CoverUrl != null && i.Categories.Count > 0 && i.StartingPrice != null);
        // What a pin on the map shows: phone, quartier (address line) and how many services the salon offers.
        page.Items.Should().OnlyContain(i => i.Phone != "" && i.AddressLine != "" && i.ServiceCount > 0);
        page.Items[0].CoverUrl.Should().StartWith("/media/demo/");
    }

    [Fact]
    public async Task Filters_by_category()
    {
        using var client = Client();

        var page = await Search(client, "category=onglerie");

        page.Items.Select(i => i.Name).Should().BeEquivalentTo("Onglerie Perle d'Ivoire", "Nail Bar Cocody Riviera");
    }

    [Fact]
    public async Task Filters_by_commune_and_by_city()
    {
        using var client = Client();
        var cocody = (await Communes(client)).Single(c => c.Name == "Cocody");

        var inCocody = await Search(client, $"communeId={cocody.Id}");
        var inCity = await Search(client, $"cityId={await AbidjanCityId(client)}&pageSize=50");

        inCocody.Items.Select(i => i.Name).Should().BeEquivalentTo(
            "Maison Éclat Coiffure", "Institut Teint Doré", "Nail Bar Cocody Riviera");
        inCocody.Items.Should().OnlyContain(i => i.CommuneName == "Cocody" && i.CityName == "Abidjan");
        inCity.Total.Should().Be(10);
    }

    [Theory]
    [InlineData("eclat", "Maison Éclat Coiffure")]
    [InlineData("ÉCLAT", "Maison Éclat Coiffure")]
    [InlineData("ebene", "Coiffure & Spa Ébène")]
    [InlineData("manucure", "Onglerie Perle d'Ivoire")]
    [InlineData("box braids", "Maison Éclat Coiffure")]
    public async Task Text_search_ignores_accents_and_case_and_matches_services(string text, string expected)
    {
        using var client = Client();

        var page = await Search(client, $"q={Uri.EscapeDataString(text)}");

        page.Items.Select(i => i.Name).Should().Contain(expected);
    }

    [Fact]
    public async Task Wildcard_characters_in_the_query_are_matched_literally()
    {
        using var client = Client();

        (await Search(client, "q=%25")).Total.Should().Be(0);
        (await Search(client, "q=_")).Total.Should().Be(0);
    }

    [Fact]
    public async Task Filters_on_online_booking()
    {
        using var client = Client();

        var page = await Search(client, "bookable=true");

        page.Items.Should().OnlyContain(i => i.BookingAvailable);
        page.Total.Should().Be(5);
    }

    [Fact]
    public async Task Proximity_search_filters_by_radius_and_reports_distance()
    {
        using var client = Client();

        var near = await Search(client, $"{Plateau}&radiusKm=1");
        var wide = await Search(client, $"{Plateau}&radiusKm=25&sort=distance&pageSize=50");

        near.Items.Should().ContainSingle().Which.Name.Should().Be("Onglerie Perle d'Ivoire");
        near.Items[0].DistanceKm.Should().Be(0);
        wide.Total.Should().Be(10);
        wide.Items.Select(i => i.DistanceKm!.Value).Should().BeInAscendingOrder();
        wide.Items[0].Name.Should().Be("Onglerie Perle d'Ivoire");
    }

    [Fact]
    public async Task Default_order_with_a_position_is_featured_first_then_nearest()
    {
        using var client = Client();

        var page = await Search(client, $"{Plateau}&pageSize=50");

        var featured = page.Items.TakeWhile(i => i.IsFeatured).ToList();
        featured.Should().HaveCount(3);
        featured.Select(i => i.DistanceKm!.Value).Should().BeInAscendingOrder();
        page.Items.Skip(3).Select(i => i.DistanceKm!.Value).Should().BeInAscendingOrder();
    }

    [Fact]
    public async Task Paginates_results()
    {
        using var client = Client();

        var third = await Search(client, "pageSize=4&page=3");
        var beyond = await Search(client, "pageSize=4&page=4");

        third.Items.Should().HaveCount(2);
        third.Total.Should().Be(10);
        third.Page.Should().Be(3);
        beyond.Items.Should().BeEmpty();
    }

    [Theory]
    [InlineData("radiusKm=5")]
    [InlineData("lat=5.3")]
    [InlineData("lat=95&lng=0")]
    [InlineData("pageSize=100")]
    [InlineData("page=0")]
    [InlineData("sort=distance")]
    [InlineData("sort=banana")]
    public async Task Rejects_invalid_search_parameters(string query)
    {
        using var client = Client();

        var response = await client.GetAsync($"/api/v1/establishments?{query}");

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Returns_the_full_establishment_page()
    {
        using var client = Client();

        var details = (await client.GetFromJsonAsync<EstablishmentDetails>("/api/v1/establishments/maison-eclat-coiffure"))!;

        details.Name.Should().Be("Maison Éclat Coiffure");
        details.CommuneName.Should().Be("Cocody");
        details.CityName.Should().Be("Abidjan");
        details.RegionName.Should().Contain("Abidjan");
        details.Phone.Should().StartWith("+225");
        details.Media.Should().HaveCount(3);
        details.Media[0].Kind.Should().Be("Photo");
        details.ServiceGroups.Select(g => g.Name).Should().Equal("Tresses", "Soins");
        details.ServiceGroups[0].Services.Should().Contain(s => s.Name == "Box braids" && s.PriceType == "From" && s.DurationMinutes == 240);
        details.OpeningHours.Should().NotBeEmpty();
        details.OpeningHours[0].DayOfWeek.Should().Be((int)DayOfWeek.Monday);
        details.OpeningHours.Should().Contain(h => h.DayOfWeek == (int)DayOfWeek.Saturday && h.OpensAt == "08:30" && h.ClosesAt == "18:00");
        details.BookingAvailable.Should().BeTrue();
    }

    [Fact]
    public async Task Unknown_and_inactive_establishments_are_not_public()
    {
        using var client = Client();
        var slug = $"ferme-{Guid.NewGuid():N}";
        await AddEstablishmentAsync(slug, EstablishmentStatus.Suspended, "Salon Fantôme Suspendu");

        (await client.GetAsync("/api/v1/establishments/does-not-exist")).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await client.GetAsync($"/api/v1/establishments/{slug}")).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await Search(client, "q=Fant%C3%B4me")).Total.Should().Be(0);
    }

    [Fact]
    public async Task Counts_views_and_clicks_per_day()
    {
        using var client = Client();
        var slug = $"stats-{Guid.NewGuid():N}";
        var id = await AddEstablishmentAsync(slug, EstablishmentStatus.Active, "Salon Statistiques");

        foreach (var type in new[] { "view", "view", "call", "directions", "Directions" })
        {
            (await client.PostAsJsonAsync($"/api/v1/establishments/{id}/events", new { type })).StatusCode.Should().Be(HttpStatusCode.NoContent);
        }

        using var scope = api.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<KekeBeautyDbContext>();
        var stats = await db.StatsDaily.AsNoTracking().SingleAsync(s => s.EstablishmentId == id);
        stats.Views.Should().Be(2);
        stats.CallClicks.Should().Be(1);
        stats.DirectionsClicks.Should().Be(2);
    }

    [Fact]
    public async Task Rejects_events_for_unknown_establishments_and_unknown_types()
    {
        using var client = Client();
        var slug = $"evt-{Guid.NewGuid():N}";
        var id = await AddEstablishmentAsync(slug, EstablishmentStatus.Active, "Salon Evenements");

        (await client.PostAsJsonAsync($"/api/v1/establishments/{Guid.NewGuid()}/events", new { type = "view" })).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await client.PostAsJsonAsync($"/api/v1/establishments/{id}/events", new { type = "share" })).StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await client.PostAsJsonAsync($"/api/v1/establishments/{id}/events", new { type = "7" })).StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    private async Task<Guid> AddEstablishmentAsync(string slug, EstablishmentStatus status, string name)
    {
        using var scope = api.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<KekeBeautyDbContext>();
        var communeId = (await db.Communes.AsNoTracking().FirstAsync(c => c.Name == "Cocody")).Id;
        var establishment = new Establishment
        {
            Name = name,
            Slug = slug,
            Description = "Établissement de test.",
            CustomerServicePhone = "+2250799999999",
            CommuneId = communeId,
            AddressLine = "Rue de test",
            Location = new Point(-3.98, 5.35) { SRID = 4326 },
            Status = status,
        };
        db.Establishments.Add(establishment);
        await db.SaveChangesAsync();
        return establishment.Id;
    }
}
