using KekeBeauty.Application.Billing;

namespace KekeBeauty.Application.Rdv;

public sealed class VerifyRdvPaiementResult
{
    public bool Success { get; init; }
    public string Status { get; init; } = "";
    public string? StatutPaiement { get; init; }
}

/// <summary>
/// Reconciliation manuelle (US3) d'un paiement RDV reste EN_COURS : interroge directement WiniPayer
/// au cas ou le callback aurait ete rate. Meme structure que VerifyAbonnementPaiementUseCase (008).
/// </summary>
public sealed class VerifyRdvPaiementUseCase
{
    private readonly IRdvPaiementRepository _repository;
    private readonly IPaymentGateway _paymentGateway;

    public VerifyRdvPaiementUseCase(IRdvPaiementRepository repository, IPaymentGateway paymentGateway)
    {
        _repository = repository;
        _paymentGateway = paymentGateway;
    }

    public async Task<VerifyRdvPaiementResult> ExecuteAsync(Guid idRdv, CancellationToken cancellationToken)
    {
        var transaction = await _repository.ObtenirParRdvAsync(idRdv, cancellationToken);
        if (transaction is null || transaction.ReferenceExterne is null)
        {
            return new VerifyRdvPaiementResult { Success = false, Status = "not_found" };
        }

        var verification = await _paymentGateway.VerifyAsync(transaction.ReferenceExterne, cancellationToken);
        if (!verification.Success)
        {
            return new VerifyRdvPaiementResult { Success = false, Status = "not_configured" };
        }

        if (!verification.EstTerminal)
        {
            return new VerifyRdvPaiementResult { Success = true, Status = "still_pending", StatutPaiement = transaction.StatutTransaction };
        }

        await _repository.MarquerPaiementAsync(transaction.ReferenceExterne, verification.PaiementReussi, verification.OperateurExterne, cancellationToken);

        return new VerifyRdvPaiementResult
        {
            Success = true,
            Status = "reconciled",
            StatutPaiement = verification.PaiementReussi ? "REUSSIE" : "ECHOUEE",
        };
    }
}
