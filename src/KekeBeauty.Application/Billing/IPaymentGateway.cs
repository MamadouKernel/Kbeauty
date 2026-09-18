namespace KekeBeauty.Application.Billing;

public sealed class PaymentInitiation
{
    public bool Success { get; init; }
    public string? CheckoutUrl { get; init; }
    public string? ReferenceExterne { get; init; }
}

public sealed class PaymentVerification
{
    public bool Success { get; init; }

    /// <summary>Etat brut renvoye par l'agregateur (ex. "success", "pending", "fail", "cancel",
    /// "expired"). L'appelant decide s'il s'agit d'un etat terminal (voir EstTerminal).</summary>
    public string? EtatBrut { get; init; }

    /// <summary>True si l'etat represente une issue definitive (succes ou echec/annulation), false
    /// si le paiement est toujours en attente (ne rien mettre a jour dans ce cas).</summary>
    public bool EstTerminal { get; init; }

    public bool PaiementReussi { get; init; }
    public string? OperateurExterne { get; init; }
}

public interface IPaymentGateway
{
    /// <summary>Genere un lien de paiement heberge (checkout WiniPayer). Retourne Success=false
    /// sans lever d'exception si l'agregateur n'est pas configure ou refuse la demande (voir
    /// research.md Decision 1). Le resultat du paiement lui-meme arrive plus tard via callback
    /// (voir research.md Decision 3 mise a jour, remplacement CinetPay).</summary>
    Task<PaymentInitiation> InitiateAsync(decimal montant, string description, CancellationToken cancellationToken);

    /// <summary>Reconciliation manuelle : interroge directement l'agregateur pour l'etat reel d'un
    /// paiement (utile si un callback a ete rate). Success=false si l'agregateur n'est pas
    /// configure ou la requete echoue, distinct de PaiementReussi qui reflete l'etat du paiement.</summary>
    Task<PaymentVerification> VerifyAsync(string referenceExterne, CancellationToken cancellationToken);
}
