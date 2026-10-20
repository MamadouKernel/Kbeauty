using FluentAssertions;
using KekeBeauty.Api.Hubs;
using KekeBeauty.SharedKernel;
using Microsoft.AspNetCore.Http.Connections;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.Extensions.DependencyInjection;
using Testcontainers.Redis;

namespace KekeBeauty.IntegrationTests;

/// <summary>Two API servers behind a load balancer: a message sent by one must reach a browser connected to the other.</summary>
[Collection(ApiCollection.Name)]
public sealed class RedisBackplaneTests(ApiFixture api)
{
    private static HubConnection Connect(WebApplicationFactory<Program> server, string token) =>
        new HubConnectionBuilder()
            .WithUrl(new Uri(server.Server.BaseAddress, HubRoutes.Notifications), options =>
            {
                options.HttpMessageHandlerFactory = _ => server.Server.CreateHandler();
                options.Transports = HttpTransportType.LongPolling;
                options.AccessTokenProvider = () => Task.FromResult<string?>(token);
            })
            .Build();

    private static async Task<bool> DeliveredAcrossServersAsync(WebApplicationFactory<Program> listener, WebApplicationFactory<Program> sender, UserSession user, TimeSpan wait)
    {
        var received = new TaskCompletionSource<NotificationPayload>(TaskCreationOptions.RunContinuationsAsynchronously);
        await using var connection = Connect(listener, user.Client.DefaultRequestHeaders.Authorization!.Parameter!);
        connection.On<NotificationPayload>("NotificationReceived", received.SetResult);
        await connection.StartAsync();
        // The hub adds the connection to the user group right after the handshake: give it a moment before sending.
        await Task.Delay(500);

        var notification = new NotificationPayload(Guid.NewGuid(), "Bonjour", "Ceci traverse deux serveurs.", "test.backplane", DateTimeOffset.UtcNow);
        await sender.Services.GetRequiredService<IRealtimeNotifier>().NotificationReceivedAsync(user.UserId, notification);

        return await Task.WhenAny(received.Task, Task.Delay(wait)) == received.Task && received.Task.Result.Id == notification.Id;
    }

    [Fact]
    public async Task A_notification_sent_by_one_server_reaches_a_browser_connected_to_another_through_redis()
    {
        await using var redis = new RedisBuilder("redis:8-alpine").Build();
        await redis.StartAsync();
        using var user = await TestAccounts.ClientAsync(api);
        var settings = redis.GetConnectionString();

        await using var serverA = api.WithWebHostBuilder(builder => builder.UseSetting("Redis:ConnectionString", settings));
        await using var serverB = api.WithWebHostBuilder(builder => builder.UseSetting("Redis:ConnectionString", settings));

        (await DeliveredAcrossServersAsync(serverA, serverB, user, TimeSpan.FromSeconds(10))).Should().BeTrue();
    }

    [Fact]
    public async Task Without_redis_each_server_only_reaches_its_own_connections()
    {
        using var user = await TestAccounts.ClientAsync(api);

        await using var serverA = api.WithWebHostBuilder(_ => { });
        await using var serverB = api.WithWebHostBuilder(_ => { });

        (await DeliveredAcrossServersAsync(serverA, serverB, user, TimeSpan.FromSeconds(2))).Should().BeFalse();
        (await DeliveredAcrossServersAsync(serverA, serverA, user, TimeSpan.FromSeconds(10))).Should().BeTrue();
    }
}
