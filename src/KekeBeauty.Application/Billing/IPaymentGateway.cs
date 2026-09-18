namespace KekeBeauty.Application.Billing;

public interface IPaymentGateway
{
    /// <summary>Tente un paiement. Retourne Succes=false sans lever d'exception si l'agregateur
    /// n'est pas configure ou refuse le paiement (voir research.md Decision 1).</summary>
    Task<bool> InitiateAsync(string canal, decimal montant, CancellationToken cancellationToken);
}
