using KekeBeauty.Application.Billing;

namespace KekeBeauty.Application.Rdv;

public sealed class InitierPaiementRdvResult
{
    public string StatutPaiement { get; init; } = "";
    public string? LienPaiement { get; init; }
    public string? ReferenceExterne { get; init; }
}

/// <summary>
/// Paiement en ligne optionnel a la demande de RDV (FR-004/FR-005). Reutilise IPaymentGateway tel
/// quel (meme agregateur WiniPayer que 008) ; echec de l'agregateur n'empeche jamais la creation du
/// RDV lui-meme, deja actee par l'appelant (RdvController) avant d'invoquer ce use case.
/// </summary>
public sealed class InitierPaiementRdvUseCase
{
    private readonly IRdvPaiementRepository _repository;
    private readonly IPaymentGateway _paymentGateway;

    public InitierPaiementRdvUseCase(IRdvPaiementRepository repository, IPaymentGateway paymentGateway)
    {
        _repository = repository;
        _paymentGateway = paymentGateway;
    }

    public async Task<InitierPaiementRdvResult> ExecuteAsync(Guid idRdv, decimal montant, CancellationToken cancellationToken)
    {
        // Idempotence (FR-009) : si une transaction existe deja pour ce RDV, on ne re-declenche pas
        // une nouvelle initiation aupres de l'agregateur, on renvoie l'etat courant.
        var existante = await _repository.ObtenirParRdvAsync(idRdv, cancellationToken);
        if (existante is not null)
        {
            return new InitierPaiementRdvResult
            {
                StatutPaiement = existante.StatutTransaction,
                ReferenceExterne = existante.ReferenceExterne,
            };
        }

        var initiation = await _paymentGateway.InitiateAsync(montant, "Rendez-vous Keke Beauty", cancellationToken);
        if (!initiation.Success || initiation.ReferenceExterne is null)
        {
            return new InitierPaiementRdvResult { StatutPaiement = "ECHOUEE" };
        }

        var transaction = await _repository.CreerOuReutiliserAsync(idRdv, montant, initiation.ReferenceExterne, cancellationToken);

        return new InitierPaiementRdvResult
        {
            StatutPaiement = transaction.StatutTransaction,
            LienPaiement = initiation.CheckoutUrl,
            ReferenceExterne = transaction.ReferenceExterne,
        };
    }
}
