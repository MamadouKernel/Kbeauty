namespace KekeBeauty.Application.Billing;

public sealed class PaymentInitiation
{
    public bool Success { get; init; }
    public string? CheckoutUrl { get; init; }
    public string? ReferenceExterne { get; init; }
}

public interface IPaymentGateway
{
    /// <summary>Genere un lien de paiement heberge (checkout WiniPayer). Retourne Success=false
    /// sans lever d'exception si l'agregateur n'est pas configure ou refuse la demande (voir
    /// research.md Decision 1). Le resultat du paiement lui-meme arrive plus tard via callback
    /// (voir research.md Decision 3 mise a jour, remplacement CinetPay).</summary>
    Task<PaymentInitiation> InitiateAsync(decimal montant, string description, CancellationToken cancellationToken);
}
