namespace KekeBeauty.Application.Billing;

public sealed class RelanceResult
{
    public bool Success { get; init; }
    public string Status { get; init; } = "";
    public bool NotificationEnvoyee { get; init; }
}

public sealed class RelanceUseCase
{
    private readonly IAbonnementRepository _repository;
    private readonly IAbonnementNotifier _notifier;

    public RelanceUseCase(IAbonnementRepository repository, IAbonnementNotifier notifier)
    {
        _repository = repository;
        _notifier = notifier;
    }

    public async Task<RelanceResult> ExecuteAsync(Guid idAbonnement, CancellationToken cancellationToken)
    {
        var abonnement = await _repository.GetByIdAsync(idAbonnement, cancellationToken);
        if (abonnement is null)
        {
            return new RelanceResult { Success = false, Status = "not_found" };
        }

        if (abonnement.StatutAbonnement != "IMPAYE")
        {
            return new RelanceResult { Success = false, Status = "not_impaye" };
        }

        var telephone = await _repository.GetGerantTelephoneAsync(idAbonnement, cancellationToken);
        var envoyee = telephone is not null && await _notifier.NotifyRelanceAsync(telephone, abonnement.Montant, cancellationToken);

        return new RelanceResult { Success = true, Status = "attempted", NotificationEnvoyee = envoyee };
    }
}
