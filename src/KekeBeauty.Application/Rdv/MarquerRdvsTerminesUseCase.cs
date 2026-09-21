using KekeBeauty.Application.Notifications;

namespace KekeBeauty.Application.Rdv;

/// <summary>
/// Feature 018 (Parcours 3) : passage automatique des RDV a TERMINE + rappel avis/pourboire.
/// Appele periodiquement par RdvCompletionBackgroundService (Api). Deux canaux independants :
/// notification in-app (toujours creee) et notification push (best-effort, seulement si le client
/// a un abonnement push actif - voir PushController).
/// </summary>
public sealed class MarquerRdvsTerminesUseCase
{
    private readonly IRdvRepository _rdvRepository;
    private readonly INotificationRepository _notificationRepository;
    private readonly IPushSubscriptionRepository _pushSubscriptionRepository;
    private readonly IPushNotificationSender _pushSender;

    public MarquerRdvsTerminesUseCase(
        IRdvRepository rdvRepository, INotificationRepository notificationRepository,
        IPushSubscriptionRepository pushSubscriptionRepository, IPushNotificationSender pushSender)
    {
        _rdvRepository = rdvRepository;
        _notificationRepository = notificationRepository;
        _pushSubscriptionRepository = pushSubscriptionRepository;
        _pushSender = pushSender;
    }

    public async Task<int> ExecuteAsync(CancellationToken cancellationToken)
    {
        var rdvsTermines = await _rdvRepository.MarquerRdvsExpiresCommeTerminesAsync(cancellationToken);

        foreach (var rdv in rdvsTermines)
        {
            var titre = "Rendez-vous terminé";
            var message = rdv.IdCollaborateur is not null
                ? $"Votre rendez-vous chez {rdv.NomEtablissement} est terminé. Laissez un avis ou un pourboire à votre praticienne !"
                : $"Votre rendez-vous chez {rdv.NomEtablissement} est terminé. Laissez un avis pour aider les autres clientes !";

            await _notificationRepository.CreerAsync(rdv.IdUtilisateurClient, titre, message, rdv.IdRdv, cancellationToken);

            var subscriptions = await _pushSubscriptionRepository.ListerParUtilisateurAsync(rdv.IdUtilisateurClient, cancellationToken);
            foreach (var subscription in subscriptions)
            {
                var (success, expired) = await _pushSender.EnvoyerAsync(subscription, titre, message, "/mes-rendez-vous", cancellationToken);
                if (!success && expired)
                {
                    await _pushSubscriptionRepository.SupprimerAsync(subscription.Endpoint, cancellationToken);
                }
            }
        }

        return rdvsTermines.Count;
    }
}
