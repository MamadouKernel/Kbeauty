using KekeBeauty.Application.Billing;

namespace KekeBeauty.Application.Rdv;

public sealed class RelancerPaiementResult
{
    public bool Success { get; init; }
    public string Status { get; init; } = "";
    public string? LienPaiement { get; init; }
}

/// <summary>US1 (015) : relance d'un paiement ECHOUEE, meme RDV, meme transaction (FR-001/002).</summary>
public sealed class RelancerPaiementRdvUseCase
{
    private readonly IRdvPaiementRepository _repository;
    private readonly IPaymentGateway _paymentGateway;

    public RelancerPaiementRdvUseCase(IRdvPaiementRepository repository, IPaymentGateway paymentGateway)
    {
        _repository = repository;
        _paymentGateway = paymentGateway;
    }

    public async Task<RelancerPaiementResult> ExecuteAsync(Guid idRdv, CancellationToken cancellationToken)
    {
        var transaction = await _repository.ObtenirParRdvAsync(idRdv, cancellationToken);
        if (transaction is null || transaction.StatutTransaction != "ECHOUEE")
        {
            return new RelancerPaiementResult { Success = false, Status = "rien_a_relancer" };
        }

        var initiation = await _paymentGateway.InitiateAsync(transaction.Montant, "Rendez-vous Keke Beauty", cancellationToken);
        if (!initiation.Success || initiation.ReferenceExterne is null)
        {
            return new RelancerPaiementResult { Success = false, Status = "payment_initiation_failed" };
        }

        var applique = await _repository.RelancerAsync(idRdv, initiation.ReferenceExterne, cancellationToken);
        if (!applique)
        {
            return new RelancerPaiementResult { Success = false, Status = "rien_a_relancer" };
        }

        return new RelancerPaiementResult { Success = true, Status = "en_cours", LienPaiement = initiation.CheckoutUrl };
    }
}
