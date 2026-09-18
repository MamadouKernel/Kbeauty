namespace KekeBeauty.Application.Onboarding;

public sealed class RejectApplicationUseCase
{
    private readonly IEtablissementRepository _repository;
    private readonly IPartnerNotifier _notifier;

    public RejectApplicationUseCase(IEtablissementRepository repository, IPartnerNotifier notifier)
    {
        _repository = repository;
        _notifier = notifier;
    }

    public async Task<ApplicationDecisionResult> ExecuteAsync(Guid idEtablissement, CancellationToken cancellationToken)
    {
        var application = await _repository.GetByIdAsync(idEtablissement, cancellationToken);
        if (application is null)
        {
            return new ApplicationDecisionResult(false, "not_found");
        }

        await _repository.UpdateStatutAsync(idEtablissement, "REJETE", cancellationToken);

        var telephone = await _repository.GetGerantTelephoneAsync(idEtablissement, cancellationToken);
        if (telephone is not null)
        {
            // FR-009 : le resultat de la notification n'empeche pas le rejet lui-meme, deja effectif.
            await _notifier.NotifyRejectionAsync(telephone, application.NomEtablissement, cancellationToken);
        }

        return new ApplicationDecisionResult(true, "REJETE");
    }
}
