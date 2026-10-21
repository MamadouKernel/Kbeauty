using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using StackExchange.Redis;
using Testcontainers.Redis;

namespace KekeBeauty.IntegrationTests;

/// <summary>
/// Two API servers behind a load balancer must enforce ONE limit between them, and a Redis that is down must neither stop the site nor remove
/// the limits.
/// </summary>
[Collection(ApiCollection.Name)]
public sealed class SharedRateLimitTests(ApiFixture api)
{
    private const string Login = "/api/v1/auth/login";

    // Malformed on purpose (no password): refused cheaply, but counted by the "auth" policy like any attempt.
    private static Task<HttpResponseMessage> AttemptAsync(HttpClient client) =>
        client.PostAsJsonAsync(Login, new { email = "someone@example.com" });

    private static async Task<List<HttpStatusCode>> AttemptsAsync(HttpClient client, int count)
    {
        var statuses = new List<HttpStatusCode>();
        for (var i = 0; i < count; i++)
        {
            statuses.Add((await AttemptAsync(client)).StatusCode);
        }

        return statuses;
    }

    [Fact]
    public async Task Two_servers_sharing_redis_enforce_one_limit_between_them()
    {
        await using var redis = new RedisBuilder("redis:8-alpine").Build();
        await redis.StartAsync();
        var connection = redis.GetConnectionString();

        await using var serverA = api.WithWebHostBuilder(b => b.UseSetting("Redis:ConnectionString", connection).UseSetting("Auth:AuthPermitPerMinute", "4"));
        await using var serverB = api.WithWebHostBuilder(b => b.UseSetting("Redis:ConnectionString", connection).UseSetting("Auth:AuthPermitPerMinute", "4"));
        using var a = serverA.CreateClient();
        using var b = serverB.CreateClient();

        // A fixed one-minute window can roll over between two calls: retry the whole sequence once on a fresh window.
        for (var round = 0; round < 2; round++)
        {
            var fromA = await AttemptsAsync(a, 2);
            var fromB = await AttemptsAsync(b, 3);
            if (fromA.Concat(fromB).Count(s => s == HttpStatusCode.TooManyRequests) == 1)
            {
                // 5 attempts over two servers with a limit of 4: exactly the fifth is refused, wherever it lands.
                fromB.Last().Should().Be(HttpStatusCode.TooManyRequests);
                return;
            }

            await Task.Delay(TimeSpan.FromSeconds(61));
        }

        throw new Xunit.Sdk.XunitException("The limit was not shared between the two servers.");
    }

    [Fact]
    public async Task Without_redis_each_server_counts_alone()
    {
        await using var serverA = api.WithWebHostBuilder(b => b.UseSetting("Auth:AuthPermitPerMinute", "4"));
        await using var serverB = api.WithWebHostBuilder(b => b.UseSetting("Auth:AuthPermitPerMinute", "4"));
        using var a = serverA.CreateClient();
        using var b = serverB.CreateClient();

        var statuses = (await AttemptsAsync(a, 2)).Concat(await AttemptsAsync(b, 3)).ToList();

        // The documented limitation of running several instances without Redis: the limit applies per instance.
        statuses.Should().NotContain(HttpStatusCode.TooManyRequests);
    }

    [Fact]
    public async Task An_unreachable_redis_neither_stops_the_site_nor_removes_the_limits()
    {
        // Nothing listens on this port: every call to the shared store fails, quickly.
        await using var server = api.WithWebHostBuilder(b => b.UseSetting("Redis:ConnectionString", "127.0.0.1:1,abortConnect=false").UseSetting("Auth:AuthPermitPerMinute", "3"));
        using var client = server.CreateClient();

        var statuses = await AttemptsAsync(client, 5);

        statuses.Should().NotContain(s => (int)s >= 500, "a dead Redis must never turn into server errors");
        statuses.Take(3).Should().AllBeEquivalentTo(HttpStatusCode.Unauthorized, "below the limit the request is judged on its own merits (here: no valid credentials)");
        statuses.Skip(3).Should().Contain(HttpStatusCode.TooManyRequests, "the limit still holds, counted by the instance itself");
    }

    [Fact]
    public async Task The_counter_expires_by_itself_so_nothing_is_left_behind_in_redis()
    {
        await using var redis = new RedisBuilder("redis:8-alpine").Build();
        await redis.StartAsync();
        await using var server = api.WithWebHostBuilder(b => b.UseSetting("Redis:ConnectionString", redis.GetConnectionString()));
        using var client = server.CreateClient();
        await AttemptAsync(client);

        using var multiplexer = await ConnectionMultiplexer.ConnectAsync(redis.GetConnectionString());
        var keys = multiplexer.GetServer(multiplexer.GetEndPoints()[0]).Keys(pattern: "keke:rl:*").ToList();

        keys.Should().NotBeEmpty();
        foreach (var key in keys)
        {
            (await multiplexer.GetDatabase().KeyTimeToLiveAsync(key)).Should().NotBeNull("a counter without a lifetime would stay for ever")
                .And.BeLessThan(TimeSpan.FromMinutes(2));
        }
    }
}
