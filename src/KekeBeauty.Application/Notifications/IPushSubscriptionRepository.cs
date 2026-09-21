namespace KekeBeauty.Application.Notifications;

public sealed class PushSubscriptionDto
{
    public string Endpoint { get; set; } = string.Empty;
    public string P256dh { get; set; } = string.Empty;
    public string Auth { get; set; } = string.Empty;
}

public interface IPushSubscriptionRepository
{
    Task EnregistrerAsync(Guid idUtilisateur, string endpoint, string p256dh, string auth, CancellationToken cancellationToken);

    Task<IReadOnlyList<PushSubscriptionDto>> ListerParUtilisateurAsync(Guid idUtilisateur, CancellationToken cancellationToken);

    /// <summary>Appele quand l'agregateur push signale un abonnement expire/revoque (410 Gone).</summary>
    Task SupprimerAsync(string endpoint, CancellationToken cancellationToken);
}
