namespace KekeBeauty.Application.Onboarding;

public sealed class ListPendingApplicationsUseCase
{
    private readonly IEtablissementRepository _repository;

    public ListPendingApplicationsUseCase(IEtablissementRepository repository)
    {
        _repository = repository;
    }

    public Task<IReadOnlyList<ApplicationSummary>> ExecuteAsync(string statutKyc, CancellationToken cancellationToken) =>
        _repository.ListByStatutAsync(statutKyc, cancellationToken);
}
