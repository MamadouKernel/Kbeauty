namespace KekeBeauty.Application.Rdv;

public sealed class DecideRdvUseCase
{
    private readonly IRdvRepository _repository;
    private readonly IRdvNotifier _notifier;

    public DecideRdvUseCase(IRdvRepository repository, IRdvNotifier notifier)
    {
        _repository = repository;
        _notifier = notifier;
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

        await NotifyAsync(idRdv, statutRdv, DateTimeOffset.UtcNow, cancellationToken);
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
    }
}
