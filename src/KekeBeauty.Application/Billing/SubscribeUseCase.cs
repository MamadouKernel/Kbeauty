namespace KekeBeauty.Application.Billing;

public sealed class SubscribeResult
{
    public bool Success { get; init; }
    public string Status { get; init; } = "";
    public Guid? IdAbonnement { get; init; }
    public string? StatutAbonnement { get; init; }
    public string? StatutTransaction { get; init; }
}

public sealed class SubscribeUseCase
{
    private readonly IAbonnementRepository _repository;
    private readonly IPaymentGateway _paymentGateway;

    public SubscribeUseCase(IAbonnementRepository repository, IPaymentGateway paymentGateway)
    {
        _repository = repository;
        _paymentGateway = paymentGateway;
    }

    public async Task<SubscribeResult> ExecuteAsync(
        Guid idEtablissement, string periodicite, string canal, CancellationToken cancellationToken)
    {
        var montant = await _repository.GetTarifStandardAsync(periodicite, cancellationToken);

        var paiementReussi = await _paymentGateway.InitiateAsync(canal, montant, cancellationToken);

        var idAbonnement = await _repository.CreerAvecTransactionAsync(
            idEtablissement, periodicite, montant, canal, paiementReussi, cancellationToken);

        if (idAbonnement is null)
        {
            return new SubscribeResult { Success = false, Status = "abonnement_actif_existant" };
        }

        return new SubscribeResult
        {
            Success = true,
            Status = "created",
            IdAbonnement = idAbonnement,
            StatutAbonnement = paiementReussi ? "ACTIF" : "IMPAYE",
            StatutTransaction = paiementReussi ? "REUSSIE" : "ECHOUEE",
        };
    }
}
