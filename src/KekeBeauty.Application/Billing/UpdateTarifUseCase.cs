namespace KekeBeauty.Application.Billing;

public sealed class UpdateTarifUseCase
{
    private readonly IAbonnementRepository _repository;

    public UpdateTarifUseCase(IAbonnementRepository repository)
    {
        _repository = repository;
    }

    public Task<IReadOnlyList<TarifStandard>> ListerAsync(CancellationToken cancellationToken) =>
        _repository.ListerTarifsAsync(cancellationToken);

    public async Task<bool> ExecuteAsync(string periodicite, decimal montant, CancellationToken cancellationToken)
    {
        if (montant <= 0)
        {
            return false;
        }

        await _repository.SetTarifStandardAsync(periodicite, montant, cancellationToken);
        return true;
    }
}
