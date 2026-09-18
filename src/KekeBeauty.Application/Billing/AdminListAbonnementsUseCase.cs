namespace KekeBeauty.Application.Billing;

public sealed class AdminListAbonnementsUseCase
{
    private readonly IAbonnementRepository _repository;

    public AdminListAbonnementsUseCase(IAbonnementRepository repository)
    {
        _repository = repository;
    }

    public Task<IReadOnlyList<AbonnementResume>> ExecuteAsync(string? statut, CancellationToken cancellationToken) =>
        _repository.ListerAsync(statut, cancellationToken);
}
