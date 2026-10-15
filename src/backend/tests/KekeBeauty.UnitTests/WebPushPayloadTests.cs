using System.Text.Json;
using FluentAssertions;
using KekeBeauty.Infrastructure.Notifications;
using KekeBeauty.SharedKernel;

namespace KekeBeauty.UnitTests;

public sealed class WebPushPayloadTests
{
    [Fact]
    public void The_payload_follows_the_angular_service_worker_notification_format()
    {
        var json = WebPushSender.BuildPayload(new PushMessage("Rappel", "Demain à 10 h", "/compte/rendez-vous", "reminder-1"));

        var notification = JsonDocument.Parse(json).RootElement.GetProperty("notification");
        notification.GetProperty("title").GetString().Should().Be("Rappel");
        notification.GetProperty("body").GetString().Should().Be("Demain à 10 h");
        notification.GetProperty("tag").GetString().Should().Be("reminder-1");
        var click = notification.GetProperty("data").GetProperty("onActionClick").GetProperty("default");
        click.GetProperty("operation").GetString().Should().Be("navigateLastFocusedOrOpen");
        click.GetProperty("url").GetString().Should().Be("/compte/rendez-vous");
    }
}
