namespace KekeBeauty.Application.Notifications;

public interface IPushNotificationSender
{
    /// <summary>Retourne (Success, Expired). Expired=true signifie que l'abonnement n'est plus
    /// valide cote navigateur (410 Gone) et doit etre supprime par l'appelant.</summary>
    Task<(bool Success, bool Expired)> EnvoyerAsync(
        PushSubscriptionDto subscription, string titre, string message, string? url, CancellationToken cancellationToken);
}
