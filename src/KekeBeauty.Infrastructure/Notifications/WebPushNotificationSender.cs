using System.Text.Json;
using KekeBeauty.Application.Notifications;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using WebPush;

namespace KekeBeauty.Infrastructure.Notifications;

/// <summary>
/// Envoie des notifications Web Push via VAPID (standard W3C, aucun compte tiers a configurer -
/// a la difference de Zavu/WiniPayer). Genere un echec explicite (Success=false) si les cles VAPID
/// ne sont pas configurees, meme garantie "jamais de fausse confirmation" que les autres
/// integrations du projet.
/// </summary>
public sealed class WebPushNotificationSender : IPushNotificationSender
{
    private readonly string? _publicKey;
    private readonly string? _privateKey;
    private readonly string _subject;
    private readonly ILogger<WebPushNotificationSender> _logger;

    public WebPushNotificationSender(IConfiguration configuration, ILogger<WebPushNotificationSender> logger)
    {
        _publicKey = configuration["Push:VapidPublicKey"];
        _privateKey = configuration["Push:VapidPrivateKey"];
        _subject = configuration["Push:VapidSubject"] ?? "mailto:contact@kekebeauty.local";
        _logger = logger;
    }

    public async Task<(bool Success, bool Expired)> EnvoyerAsync(
        PushSubscriptionDto subscription, string titre, string message, string? url, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(_publicKey) || string.IsNullOrWhiteSpace(_privateKey))
        {
            _logger.LogWarning("Envoi push ignore : cles VAPID non configurees.");
            return (false, false);
        }

        try
        {
            var client = new WebPushClient();
            var vapidDetails = new VapidDetails(_subject, _publicKey, _privateKey);
            var pushSubscription = new PushSubscription(subscription.Endpoint, subscription.P256dh, subscription.Auth);

            var payload = JsonSerializer.Serialize(new { titre, message, url });
            await client.SendNotificationAsync(pushSubscription, payload, vapidDetails, cancellationToken);

            return (true, false);
        }
        catch (WebPushException ex) when (ex.StatusCode is System.Net.HttpStatusCode.Gone or System.Net.HttpStatusCode.NotFound)
        {
            _logger.LogInformation("Abonnement push expire/revoque (endpoint {Endpoint}).", subscription.Endpoint);
            return (false, true);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Envoi push echoue (exception).");
            return (false, false);
        }
    }
}
