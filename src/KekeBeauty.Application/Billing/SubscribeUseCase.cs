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
    private readonly PlanAccessService _planAccessService;

    public SubscribeUseCase(IAbonnementRepository repository, IPaymentGateway paymentGateway, PlanAccessService planAccessService)
    {
        _repository = repository;
        _paymentGateway = paymentGateway;
        _planAccessService = planAccessService;
    }

    public async Task<SubscribeResult> ExecuteAsync(Guid idEtablissement, string formule, string periodicite, CancellationToken cancellationToken)
    {
        var plan = await _planAccessService.GetEntitlementsAsync(formule, cancellationToken);
        if (plan is null || !plan.EstActif)
        {
            return new SubscribeResult { Success = false, Status = "formule_introuvable" };
        }

        var montant = periodicite == "ANNUEL" ? plan.TarifAnnuelEffectif : plan.TarifMensuelEffectif;
        if (montant is null)
        {
            return new SubscribeResult { Success = false, Status = "formule_non_souscriptible" };
        }

        var initiation = await _paymentGateway.InitiateAsync(
            montant.Value, $"Abonnement Keke Beauty {plan.Libelle} ({periodicite})", cancellationToken);

        if (!initiation.Success || initiation.ReferenceExterne is null)
        {
            return new SubscribeResult { Success = false, Status = "payment_initiation_failed" };
        }

        var idAbonnement = await _repository.CreerAvecTransactionAsync(
            idEtablissement, formule, periodicite, montant.Value, initiation.ReferenceExterne, cancellationToken);

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
