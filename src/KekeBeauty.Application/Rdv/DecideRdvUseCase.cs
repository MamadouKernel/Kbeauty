using KekeBeauty.Application.Notifications;

namespace KekeBeauty.Application.Rdv;

public sealed class DecideRdvUseCase
{
    private readonly IRdvRepository _repository;
    private readonly IRdvNotifier _notifier;
    private readonly ClientNotificationService _notificationService;

    public DecideRdvUseCase(IRdvRepository repository, IRdvNotifier notifier, ClientNotificationService notificationService)
    {
        _repository = repository;
        _notifier = notifier;
        _notificationService = notificationService;
    }

    public async Task<DecideRdvResult> ConfirmerAsync(Guid idEtablissement, Guid idRdv, CancellationToken cancellationToken) =>
        await ChangeStatutAsync(idEtablissement, idRdv, "CONFIRME", cancellationToken);

    public async Task<DecideRdvResult> RefuserAsync(Guid idEtablissement, Guid idRdv, CancellationToken cancellationToken) =>
        await ChangeStatutAsync(idEtablissement, idRdv, "REFUSE", cancellationToken);

    public async Task<DecideRdvResult> ReprogrammerAsync(
        Guid idEtablissement, Guid idRdv, DateTimeOffset nouvelleDateHeureDebut, CancellationToken cancellationToken)
    {
        var updated = await _repository.RescheduleAsync(idEtablissement, idRdv, nouvelleDateHeureDebut, cancellationToken);
        if (!updated)
        {
            return new DecideRdvResult(false, "slot_unavailable_or_not_found");
        }

        await NotifyAsync(idRdv, "DEMANDE (reprogramme)", nouvelleDateHeureDebut, cancellationToken);
        return new DecideRdvResult(true, "DEMANDE");
    }

    private async Task<DecideRdvResult> ChangeStatutAsync(Guid idEtablissement, Guid idRdv, string statutRdv, CancellationToken cancellationToken)
    {
        var updated = await _repository.UpdateStatutAsync(idEtablissement, idRdv, statutRdv, cancellationToken);
        if (!updated)
        {
            return new DecideRdvResult(false, "not_found");
        }

        var dateHeureDebut = await _repository.GetDateHeureDebutAsync(idRdv, cancellationToken) ?? DateTimeOffset.UtcNow;
        await NotifyAsync(idRdv, statutRdv, dateHeureDebut, cancellationToken);
        return new DecideRdvResult(true, statutRdv);
    }

    private async Task NotifyAsync(Guid idRdv, string statutRdv, DateTimeOffset dateHeureDebut, CancellationToken cancellationToken)
    {
        var telephone = await _repository.GetClientTelephoneAsync(idRdv, cancellationToken);
        if (telephone is not null)
        {
            // FR-006 : la notification n'empeche pas la decision, deja effective.
            await _notifier.NotifyDecisionAsync(telephone, statutRdv, dateHeureDebut, cancellationToken);
        }

        // Feature 018 : notification in-app, canal independant du SMS/WhatsApp (Zavu) ci-dessus -
        // seul canal reellement fonctionnel tant que Zavu n'est pas configure.
        var idClient = await _repository.GetClientIdAsync(idRdv, cancellationToken);
        if (idClient is not null)
        {
            var (titre, message) = LibelleNotification(statutRdv, dateHeureDebut);
            await _notificationService.SendAsync(idClient.Value, titre, message, idRdv, $"/mes-rendez-vous/{idRdv}", cancellationToken);
        }
    }

    private static (string Titre, string Message) LibelleNotification(string statutRdv, DateTimeOffset dateHeureDebut) => statutRdv switch
    {
        "CONFIRME" => ("Rendez-vous confirmé", $"Votre rendez-vous du {dateHeureDebut:dd/MM/yyyy à HH:mm} a été confirmé par l'établissement."),
        "REFUSE" => ("Rendez-vous refusé", $"Votre demande de rendez-vous du {dateHeureDebut:dd/MM/yyyy à HH:mm} a été refusée par l'établissement."),
        _ => ("Rendez-vous reprogrammé", $"Votre rendez-vous a été reprogrammé au {dateHeureDebut:dd/MM/yyyy à HH:mm}."),
    };
}

