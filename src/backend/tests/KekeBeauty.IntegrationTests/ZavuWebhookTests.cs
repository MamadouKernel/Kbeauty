using System.Net;
using System.Security.Cryptography;
using System.Text;
using FluentAssertions;
using KekeBeauty.Infrastructure.Persistence;
using KekeBeauty.Modules.Notifications.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace KekeBeauty.IntegrationTests;

[Collection(ApiCollection.Name)]
public sealed class ZavuWebhookTests(ApiFixture api)
{
    private const string Url = "/api/v1/webhooks/whatsapp/zavu";

    private string Sign(string body, string? secret = null, DateTimeOffset? at = null)
    {
        var t = (at ?? api.Clock.GetUtcNow()).ToUnixTimeSeconds();
        var mac = HMACSHA256.HashData(Encoding.UTF8.GetBytes(secret ?? TestSecrets.ZavuWebhookSecret), Encoding.UTF8.GetBytes($"{t}.{body}"));
        return $"t={t},v2={Convert.ToHexStringLower(mac)}";
    }

    private async Task<HttpResponseMessage> PostAsync(string body, string? signature = null, bool signed = true)
    {
        using var client = api.CreateClient();
        using var request = new HttpRequestMessage(HttpMethod.Post, Url) { Content = new StringContent(body, Encoding.UTF8, "application/json") };
        if (signed)
        {
            request.Headers.Add("X-Zavu-Signature", signature ?? Sign(body));
        }

        return await client.SendAsync(request);
    }

    private static string Event(string type, object data, string? id = null) =>
        System.Text.Json.JsonSerializer.Serialize(new { id = id ?? $"evt_{Guid.NewGuid():N}", type, timestamp = 0, senderId = "snd_1", projectId = "prj_1", data });

    private async Task<(Guid UserId, string MessageId)> WhatsAppNotificationAsync(DeliveryStatus status = DeliveryStatus.Sent)
    {
        var messageId = $"msg_{Guid.NewGuid():N}";
        var userId = Guid.NewGuid();
        await using var scope = api.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<KekeBeautyDbContext>();
        db.Notifications.Add(new Notification
        {
            UserId = userId, Channel = NotificationChannel.WhatsApp, TemplateCode = "appointment.confirmed", Title = "t", Body = "b",
            Status = status, ProviderMessageId = messageId, SentAt = api.Clock.GetUtcNow(),
        });
        await db.SaveChangesAsync();
        return (userId, messageId);
    }

    private async Task<Notification> NotificationAsync(string messageId)
    {
        await using var scope = api.Services.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<KekeBeautyDbContext>().Notifications.AsNoTracking().SingleAsync(n => n.ProviderMessageId == messageId);
    }

    [Fact]
    public async Task Unsigned_wrongly_signed_stale_or_replayed_calls_are_refused_and_change_nothing()
    {
        var (_, messageId) = await WhatsAppNotificationAsync();
        var body = Event("message.delivered", new { messageId, to = "+2250700001234", channel = "whatsapp", status = "delivered" });

        (await PostAsync(body, signed: false)).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        (await PostAsync(body, Sign(body, "wrong-secret"))).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        (await PostAsync(body, Sign(body, at: api.Clock.GetUtcNow().AddMinutes(-10)))).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        (await PostAsync(body, Sign(body + " "))).StatusCode.Should().Be(HttpStatusCode.Unauthorized);

        (await NotificationAsync(messageId)).Status.Should().Be(DeliveryStatus.Sent);
    }

    [Fact]
    public async Task A_delivery_report_marks_the_message_delivered_and_a_failure_report_records_the_error()
    {
        var (_, delivered) = await WhatsAppNotificationAsync();
        var (_, failed) = await WhatsAppNotificationAsync();

        (await PostAsync(Event("message.delivered", new { messageId = delivered, to = "+2250700001234", channel = "whatsapp", status = "delivered" }))).StatusCode.Should().Be(HttpStatusCode.OK);
        (await PostAsync(Event("message.failed", new { messageId = failed, to = "+2250700001234", channel = "whatsapp", status = "failed", errorCode = "131026" }))).StatusCode.Should().Be(HttpStatusCode.OK);

        (await NotificationAsync(delivered)).Status.Should().Be(DeliveryStatus.Delivered);
        var row = await NotificationAsync(failed);
        row.Status.Should().Be(DeliveryStatus.Failed);
        row.Error.Should().Contain("131026");
    }

    [Fact]
    public async Task A_late_failure_never_downgrades_a_delivered_message_and_a_repeated_event_is_acknowledged_once()
    {
        var (_, messageId) = await WhatsAppNotificationAsync();
        var id = $"evt_{Guid.NewGuid():N}";
        var delivered = Event("message.delivered", new { messageId, to = "+2250700001234", channel = "whatsapp", status = "delivered" }, id);

        (await PostAsync(delivered)).StatusCode.Should().Be(HttpStatusCode.OK);
        (await PostAsync(delivered)).StatusCode.Should().Be(HttpStatusCode.OK);
        (await PostAsync(Event("message.failed", new { messageId, to = "+2250700001234", channel = "whatsapp", status = "failed", errorCode = "x" }))).StatusCode.Should().Be(HttpStatusCode.OK);

        (await NotificationAsync(messageId)).Status.Should().Be(DeliveryStatus.Delivered);
    }

    [Fact]
    public async Task Events_about_unknown_messages_other_channels_or_unknown_types_are_acknowledged_and_ignored()
    {
        (await PostAsync(Event("message.delivered", new { messageId = "unknown", to = "+2250700001234", channel = "whatsapp", status = "delivered" }))).StatusCode.Should().Be(HttpStatusCode.OK);
        (await PostAsync(Event("broadcast.status_changed", new { broadcastId = "b1" }))).StatusCode.Should().Be(HttpStatusCode.OK);
        (await PostAsync(Event("template.status_changed", new { templateName = "keke_otp", language = "fr", currentStatus = "approved" }))).StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task A_message_id_of_an_sms_cannot_be_moved_through_the_whatsapp_webhook()
    {
        var messageId = $"msg_{Guid.NewGuid():N}";
        await using (var scope = api.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<KekeBeautyDbContext>();
            db.Notifications.Add(new Notification { UserId = Guid.NewGuid(), Channel = NotificationChannel.Sms, TemplateCode = "x", Title = "t", Body = "b", Status = DeliveryStatus.Sent, ProviderMessageId = messageId });
            await db.SaveChangesAsync();
        }

        await PostAsync(Event("message.failed", new { messageId, to = "+2250700001234", channel = "whatsapp", status = "failed" }));

        (await NotificationAsync(messageId)).Status.Should().Be(DeliveryStatus.Sent);
    }

    [Fact]
    public async Task Malformed_signed_bodies_get_400_and_oversized_bodies_are_refused_before_parsing()
    {
        (await PostAsync("not json")).StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await PostAsync("{\"id\":\"e\"}")).StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await PostAsync("[1,2]")).StatusCode.Should().Be(HttpStatusCode.BadRequest);

        var huge = "{\"id\":\"e\",\"type\":\"message.delivered\",\"data\":{\"x\":\"" + new string('a', 70 * 1024) + "\"}}";
        (await PostAsync(huge)).StatusCode.Should().Be(HttpStatusCode.RequestEntityTooLarge);
    }

    [Fact]
    public async Task A_stop_reply_from_a_known_number_switches_whatsapp_off_and_other_replies_do_nothing()
    {
        using var client = await TestAccounts.ClientAsync(api);
        var phone = $"+22507{Random.Shared.Next(10_000_000, 99_999_999)}";
        await using (var scope = api.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<KekeBeautyDbContext>();
            await db.Users.Where(u => u.Id == client.UserId).ExecuteUpdateAsync(set => set.SetProperty(u => u.PhoneNumber, phone));
        }

        async Task<bool> WhatsAppOnAsync()
        {
            await using var scope = api.Services.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<KekeBeautyDbContext>();
            return (await db.NotificationPreferences.AsNoTracking().Where(p => p.UserId == client.UserId).Select(p => p.WhatsApp).ToListAsync()).DefaultIfEmpty(true).Single();
        }

        object Inbound(string from, string text) => new { messageId = $"in_{Guid.NewGuid():N}", from, to = "+2250100000000", channel = "whatsapp", messageType = "text", text, status = "received" };

        (await PostAsync(Event("message.inbound", Inbound(phone, "Merci beaucoup")))).StatusCode.Should().Be(HttpStatusCode.OK);
        (await PostAsync(Event("message.inbound", Inbound("+2250799999998", "STOP")))).StatusCode.Should().Be(HttpStatusCode.OK);
        (await WhatsAppOnAsync()).Should().BeTrue();

        (await PostAsync(Event("message.inbound", Inbound(phone, " Stop. ")))).StatusCode.Should().Be(HttpStatusCode.OK);
        (await WhatsAppOnAsync()).Should().BeFalse();
    }
}
