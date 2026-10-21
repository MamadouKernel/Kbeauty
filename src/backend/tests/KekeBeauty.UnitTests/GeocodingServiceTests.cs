using System.Net;
using System.Text;
using FluentAssertions;
using KekeBeauty.Infrastructure.Geo;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;

namespace KekeBeauty.UnitTests;

public sealed class GeocodingServiceTests
{
    private sealed class Handler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
    {
        public List<HttpRequestMessage> Requests { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests.Add(request);
            return Task.FromResult(respond(request));
        }
    }

    private sealed class Factory(Handler handler) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new(handler, disposeHandler: false) { BaseAddress = new Uri("https://nominatim.test/") };
    }

    private static HttpResponseMessage Json(string body, HttpStatusCode status = HttpStatusCode.OK) =>
        new(status) { Content = new StringContent(body, Encoding.UTF8, "application/json") };

    private static GeocodingService Service(Handler handler) =>
        new(new Factory(handler), new MemoryCache(new MemoryCacheOptions()), Options.Create(new GeocodingOptions { MinIntervalMilliseconds = 0 }));

    [Fact]
    public async Task Search_returns_clean_places_limited_to_the_configured_country_and_caches_the_answer()
    {
        var handler = new Handler(_ => Json("""
            [{"display_name":"Cocody, Abidjan","lat":"5.3599","lon":"-3.9999"},
             {"display_name":"","lat":"1","lon":"1"},
             {"display_name":"Hors limites","lat":"95","lon":"1"},
             {"display_name":"Sans coordonnées"}]
            """));
        var service = Service(handler);

        var result = await service.SearchAsync("  Cocody   Abidjan ", default);

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().ContainSingle().Which.Should().Be(new GeoPlace("Cocody, Abidjan", 5.3599, -3.9999));
        handler.Requests.Should().ContainSingle().Which.RequestUri!.Query.Should().Contain("countrycodes=ci").And.Contain("q=Cocody%20Abidjan");

        // Same question, any case: answered from the cache, the provider is not called again.
        (await service.SearchAsync("cocody abidjan", default)).IsSuccess.Should().BeTrue();
        handler.Requests.Should().HaveCount(1);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("  ab  ")]
    [InlineData("rue\u0000injectée")]
    public async Task Search_refuses_input_that_is_empty_too_short_or_made_of_control_characters(string? query)
    {
        var handler = new Handler(_ => Json("[]"));

        var result = await Service(handler).SearchAsync(query, default);

        result.IsFailure.Should().BeTrue();
        handler.Requests.Should().BeEmpty();
    }

    [Fact]
    public async Task Reverse_rounds_the_point_and_refuses_impossible_coordinates()
    {
        var handler = new Handler(_ => Json("""{"display_name":"Boulevard Latrille, Cocody","lat":"5.36","lon":"-3.99"}"""));
        var service = Service(handler);

        (await service.ReverseAsync(5.3600012, -3.9900049, default)).Value.Should().ContainSingle();
        handler.Requests.Single().RequestUri!.Query.Should().Contain("lat=5.36").And.Contain("lon=-3.99");
        (await service.ReverseAsync(5.36000, -3.99000, default)).IsSuccess.Should().BeTrue();
        handler.Requests.Should().HaveCount(1);

        foreach (var (lat, lon) in new[] { (91.0, 0.0), (0.0, 181.0), (double.NaN, 0.0), (0.0, double.PositiveInfinity) })
        {
            (await service.ReverseAsync(lat, lon, default)).IsFailure.Should().BeTrue();
        }
    }

    [Fact]
    public async Task A_failing_provider_gives_a_clear_error_and_is_not_cached()
    {
        var failing = true;
        var handler = new Handler(_ => failing ? Json("oops", HttpStatusCode.ServiceUnavailable) : Json("""[{"display_name":"Plateau","lat":"5.32","lon":"-4.02"}]"""));
        var service = Service(handler);

        var down = await service.SearchAsync("Plateau", default);
        down.IsFailure.Should().BeTrue();
        down.Error.Code.Should().Be("geo.provider_error");

        failing = false;
        (await service.SearchAsync("Plateau", default)).Value.Should().ContainSingle();
    }

    [Fact]
    public async Task A_garbled_answer_is_an_error_not_a_crash()
    {
        var down = await Service(new Handler(_ => Json("<html>not json</html>"))).SearchAsync("Plateau", default);

        down.IsFailure.Should().BeTrue();
        down.Error.Code.Should().Be("geo.provider_error");
    }
}
