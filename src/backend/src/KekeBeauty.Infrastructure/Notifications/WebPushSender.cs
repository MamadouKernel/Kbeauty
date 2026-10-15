using System.Net;
using System.Text.Json;
using KekeBeauty.SharedKernel;
using Lib.Net.Http.WebPush;
using Lib.Net.Http.WebPush.Authentication;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using LibMessage = Lib.Net.Http.WebPush.PushMessage;
using LibSubscription = Lib.Net.Http.WebPush.PushSubscription;
using Message = KekeBeauty.SharedKernel.PushMessage;

namespace KekeBeauty.Infrastructure.Notifications;

/// <summary>Delivers Web Push messages (RFC 8030, encrypted per RFC 8291, VAPID-authenticated) to the browsers' push services.</summary>
public sealed class WebPushSender(HttpClient http, VapidKeyStore keys, IOptions<PushOptions> options, ILogger<WebPushSender> logger) : IPushSender
{
    private static readonly TimeSpan TimeToLive = TimeSpan.FromHours(12);

    /// <summary>
    /// The JSON the Angular service worker understands: it shows the notification by itself and opens (or focuses) the page
    /// when it is clicked, so the web app needs no push handler of its own.
    /// </summary>
    public static string BuildPayload(Message message) => JsonSerializer.Serialize(new
    {
        notification = new
        {
            title = message.Title,
            body = message.Body,
            icon = "/icons/icon-192.png",
            badge = "/icons/icon-192.png",
            lang = "fr",
            tag = message.Tag,
            data = new
            {
                url = message.Url,
                onActionClick = new { @default = new { operation = "navigateLastFocusedOrOpen", url = message.Url } },
            },
        },
    });

    public async Task<PushOutcome> SendAsync(PushSubscriptionInfo subscription, Message message, CancellationToken cancellationToken = default)
    {
        var vapid = await keys.GetAsync(cancellationToken);
        var client = new PushServiceClient(http);
        var target = new LibSubscription { Endpoint = subscription.Endpoint };
        target.SetKey(PushEncryptionKeyName.P256DH, subscription.P256dh);
        target.SetKey(PushEncryptionKeyName.Auth, subscription.Auth);

        var payload = BuildPayload(message);
        var push = new LibMessage(payload)
        {
            Urgency = PushMessageUrgency.Normal,
            TimeToLive = (int)TimeToLive.TotalSeconds,
            Topic = message.Tag.Length > 32 ? message.Tag[..32] : message.Tag,
        };
        var authentication = new VapidAuthentication(vapid.PublicKey, vapid.PrivateKey) { Subject = options.Value.Subject };

        try
        {
            await client.RequestPushMessageDeliveryAsync(target, push, authentication, cancellationToken);
            return PushOutcome.Sent;
        }
        catch (PushServiceClientException ex) when (ex.StatusCode is HttpStatusCode.NotFound or HttpStatusCode.Gone)
        {
            return PushOutcome.Gone;
        }
        catch (Exception ex) when (ex is PushServiceClientException or HttpRequestException or TaskCanceledException)
        {
            logger.LogWarning(ex, "Web Push delivery failed");
            return PushOutcome.Failed;
        }
    }
}
