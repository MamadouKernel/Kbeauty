using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Npgsql;

namespace KekeBeauty.IntegrationTests;

[Collection(ApiCollection.Name)]
public sealed class Lot0Tests(ApiFixture api)
{
    private sealed record Category(Guid Id, string Name, string Slug, string Icon);
    private sealed record Country(Guid Id, string Iso2, string Name, string PhonePrefix, string CurrencyCode);
    private sealed record GeoItem(Guid Id, string Name);

    [Fact]
    public async Task Ready_health_check_is_healthy()
    {
        using var client = api.CreateClient();

        var response = await client.GetAsync("/health/ready");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Live_health_check_is_healthy()
    {
        using var client = api.CreateClient();

        var response = await client.GetAsync("/health/live");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Required_postgres_extensions_are_installed()
    {
        await using var connection = new NpgsqlConnection(api.ConnectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand("SELECT extname FROM pg_extension", connection);
        var installed = new List<string>();
        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            installed.Add(reader.GetString(0));
        }

        installed.Should().Contain(["postgis", "btree_gist", "citext", "unaccent", "pg_trgm"]);
    }

    [Fact]
    public async Task Categories_are_seeded_in_display_order()
    {
        using var client = api.CreateClient();

        var categories = await client.GetFromJsonAsync<List<Category>>("/api/v1/categories");

        categories.Should().NotBeNull();
        categories!.Select(c => c.Slug).Should().Equal(
            "salon-de-coiffure", "espace-spa", "massage", "onglerie", "barbier", "esthetique-soins-visage", "maquillage");
    }

    [Fact]
    public async Task Geography_cascade_reaches_abidjan_communes()
    {
        using var client = api.CreateClient();

        var countries = await client.GetFromJsonAsync<List<Country>>("/api/v1/geo/countries");
        var ci = countries!.Should().ContainSingle().Subject;
        ci.Iso2.Should().Be("CI");
        ci.PhonePrefix.Should().Be("+225");
        ci.CurrencyCode.Should().Be("XOF");

        var regions = await client.GetFromJsonAsync<List<GeoItem>>($"/api/v1/geo/countries/{ci.Id}/regions");
        var abidjanRegion = regions!.Single(r => r.Name.Contains("Abidjan", StringComparison.Ordinal));

        var cities = await client.GetFromJsonAsync<List<GeoItem>>($"/api/v1/geo/regions/{abidjanRegion.Id}/cities");
        var abidjan = cities!.Should().ContainSingle().Subject;

        var communes = await client.GetFromJsonAsync<List<GeoItem>>($"/api/v1/geo/cities/{abidjan.Id}/communes");
        communes!.Should().HaveCount(13);
        communes.Select(c => c.Name).Should().Contain(["Cocody", "Yopougon", "Plateau"]);
    }

    [Fact]
    public async Task Openapi_document_is_only_published_in_development()
    {
        using var client = api.CreateClient();

        var response = await client.GetAsync("/openapi/v1.json");

        // The tests run in the "Testing" environment: like Production, it does not publish the API contract.
        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }
}
