using KekeBeauty.Application.Billing;

namespace KekeBeauty.Application.Rdv;

public sealed class LaisserPourboireResult
{
    public bool Success { get; init; }
    public string Status { get; init; } = string.Empty;
    public string? LienPaiement { get; init; }
    public Guid? IdPourboire { get; init; }
}

/// <summary>
/// Feature 018 (Parcours 3) : pourboire directement attribue a une collaboratrice, 0% de commission
/// plateforme. Constat honnete (voir specs/018.../spec.md, Assumptions) : WiniPayer est un
/// encaisseur marchand pour le compte de Keke Beauty, pas une plateforme de paiement P2P vers un
/// tiers individuel - il n'existe donc pas d'API pour transferer directement les fonds sur le
/// compte mobile money personnel de la collaboratrice. Le paiement transite par le meme checkout
/// marchand que le RDV lui-meme (comme InitierPaiementRdvUseCase), et le montant integral (aucune
/// retenue de commission, a la difference du paiement de la prestation) est trace pour un
/// reversement manuel a la collaboratrice - meme limitation documentee que le remboursement
/// (AnnulerRdvUseCase) : automatisation du virement final hors de portee sans API de payout P2P.
/// </summary>
public sealed class LaisserPourboireUseCase
{
    private readonly IPourboireRepository _repository;
    private readonly IPaymentGateway _paymentGateway;

    public LaisserPourboireUseCase(IPourboireRepository repository, IPaymentGateway paymentGateway)
    {
        _repository = repository;
        _paymentGateway = paymentGateway;
    }

    public async Task<LaisserPourboireResult> ExecuteAsync(Guid idRdv, Guid idCollaborateur, decimal montant, CancellationToken cancellationToken)
    {
        if (montant <= 0)
        {
            return new LaisserPourboireResult { Success = false, Status = "montant_invalide" };
        }

        if (!await _repository.CollaboratriceValidePourRdvAsync(idRdv, idCollaborateur, cancellationToken))
        {
            return new LaisserPourboireResult { Success = false, Status = "collaboratrice_invalide" };
        }

        var initiation = await _paymentGateway.InitiateAsync(montant, "Pourboire Keke Beauty", cancellationToken);
        if (!initiation.Success || initiation.ReferenceExterne is null)
        {
            return new LaisserPourboireResult { Success = false, Status = "payment_initiation_failed" };
        }

        var idPourboire = await _repository.CreerAsync(idRdv, idCollaborateur, montant, initiation.ReferenceExterne, cancellationToken);

        return new LaisserPourboireResult
        {
            Success = true,
            Status = "en_attente",
            LienPaiement = initiation.CheckoutUrl,
            IdPourboire = idPourboire,
        };
    }
}
