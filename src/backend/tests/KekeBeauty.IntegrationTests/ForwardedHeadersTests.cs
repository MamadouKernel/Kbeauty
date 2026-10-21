using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;

namespace KekeBeauty.IntegrationTests;

/// <summary>
/// A rotating X-Forwarded-For must not give a caller a fresh rate-limit bucket per request. The header is believed only from the reverse proxy,
/// which sits on a private network; from a public address it is ignored.
/// </summary>
[Collection(ApiCollection.Name)]
public sealed class ForwardedHeadersTests(ApiFixture api)
{
    /// <summary>Makes every request look as if it came from <paramref name="source"/>, before anything else in the pipeline.</summary>
    private sealed class FakePeer(string source) : IStartupFilter
    {
        public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next) => app =>
        {
            app.Use(async (context, continuation) =>
            {
                context.Connection.RemoteIpAddress = IPAddress.Parse(source);
                await continuation();
            });
            next(app);
        };
    }

    private async Task<List<HttpStatusCode>> RotatingAttemptsAsync(string peer, bool trustForwarded = true, int count = 8)
    {
        await using var server = api.WithWebHostBuilder(builder => builder
            .UseSetting("Proxy:TrustForwardedHeaders", trustForwarded ? "true" : "false")
            .UseSetting("Auth:AuthPermitPerMinute", "3")
            .ConfigureServices(services => services.AddSingleton<IStartupFilter>(new FakePeer(peer))));
        using var client = server.CreateClient();

        var statuses = new List<HttpStatusCode>();
        for (var i = 1; i <= count; i++)
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/auth/login") { Content = JsonContent.Create(new { email = "someone@example.com" }) };
            request.Headers.Add("X-Forwarded-For", $"198.51.100.{i}");
            statuses.Add((await client.SendAsync(request)).StatusCode);
        }

        return statuses;
    }

    [Fact]
    public async Task From_a_public_address_a_rotating_forwarded_header_is_ignored_and_the_limit_holds()
    {
        var statuses = await RotatingAttemptsAsync("203.0.113.50");

        statuses.Should().Contain(HttpStatusCode.TooManyRequests, "the caller is judged on its real address, whatever it writes in the header");
        statuses.Take(3).Should().NotContain(HttpStatusCode.TooManyRequests);
    }

    [Fact]
    public async Task From_the_private_address_of_the_reverse_proxy_the_forwarded_header_names_the_real_client()
    {
        // Each forwarded address is a different client of the proxy: none of them reaches the limit, which is what a proxy in front means.
        var statuses = await RotatingAttemptsAsync("172.18.0.5");

        statuses.Should().NotContain(HttpStatusCode.TooManyRequests);
    }

    [Fact]
    public async Task Without_proxy_trust_the_header_is_never_read_at_all()
    {
        var statuses = await RotatingAttemptsAsync("172.18.0.5", trustForwarded: false);

        statuses.Should().Contain(HttpStatusCode.TooManyRequests);
    }
}
