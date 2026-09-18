namespace KekeBeauty.Application.Billing;

public sealed class SubscribeResult
{
    public bool Success { get; init; }
    public string Status { get; init; } = "";
    public Guid? IdAbonnement { get; init; }
    public string? StatutAbonnement { get; init; }
    public string? CheckoutUrl { get; init; }
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

    public async Task<SubscribeResult> ExecuteAsync(Guid idEtablissement, string periodicite, CancellationToken cancellationToken)
    {
        var montant = await _repository.GetTarifStandardAsync(periodicite, cancellationToken);

        var initiation = await _paymentGateway.InitiateAsync(
            montant, $"Abonnement Keke Beauty ({periodicite})", cancellationToken);

        if (!initiation.Success || initiation.ReferenceExterne is null)
        {
            return new SubscribeResult { Success = false, Status = "payment_initiation_failed" };
        }

        var idAbonnement = await _repository.CreerAvecTransactionAsync(
            idEtablissement, periodicite, montant, initiation.ReferenceExterne, cancellationToken);

        if (idAbonnement is null)
        {
            return new SubscribeResult { Success = false, Status = "abonnement_actif_existant" };
        }

        return new SubscribeResult
        {
            Success = true,
            Status = "created",
            IdAbonnement = idAbonnement,
            StatutAbonnement = "IMPAYE",
            CheckoutUrl = initiation.CheckoutUrl,
        };
    }
}
