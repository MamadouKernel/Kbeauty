namespace KekeBeauty.Application.Billing;

public sealed class VerifyPaiementResult
{
    public bool Success { get; init; }
    public string Status { get; init; } = "";
    public string? StatutAbonnement { get; init; }
}

/// <summary>
/// Reconciliation manuelle (admin) d'un abonnement IMPAYE : interroge directement WiniPayer au cas
/// ou le callback aurait ete rate (panne reseau, indisponibilite temporaire, etc.).
/// </summary>
public sealed class VerifyAbonnementPaiementUseCase
{
    private readonly IAbonnementRepository _repository;
    private readonly IPaymentGateway _paymentGateway;

    public VerifyAbonnementPaiementUseCase(IAbonnementRepository repository, IPaymentGateway paymentGateway)
    {
        _repository = repository;
        _paymentGateway = paymentGateway;
    }

    public async Task<VerifyPaiementResult> ExecuteAsync(Guid idAbonnement, CancellationToken cancellationToken)
    {
        var abonnement = await _repository.GetByIdAsync(idAbonnement, cancellationToken);
        if (abonnement is null)
        {
            return new VerifyPaiementResult { Success = false, Status = "not_found" };
        }

        var referenceExterne = await _repository.GetReferenceExterneEnCoursAsync(idAbonnement, cancellationToken);
        if (referenceExterne is null)
        {
            return new VerifyPaiementResult { Success = false, Status = "no_pending_transaction" };
        }

        var verification = await _paymentGateway.VerifyAsync(referenceExterne, cancellationToken);
        if (!verification.Success)
        {
            return new VerifyPaiementResult { Success = false, Status = "verification_failed" };
        }

        if (!verification.EstTerminal)
        {
            return new VerifyPaiementResult { Success = true, Status = "still_pending", StatutAbonnement = abonnement.StatutAbonnement };
        }

        await _repository.MarquerPaiementAsync(referenceExterne, verification.PaiementReussi, verification.OperateurExterne, cancellationToken);

        return new VerifyPaiementResult
        {
            Success = true,
            Status = "reconciled",
            StatutAbonnement = verification.PaiementReussi ? "ACTIF" : "IMPAYE",
        };
    }
}
