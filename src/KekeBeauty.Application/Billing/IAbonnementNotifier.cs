namespace KekeBeauty.Application.Billing;

public interface IAbonnementNotifier
{
    /// <summary>Notifie le gerant d'un abonnement impaye (relance, FR-006). Retourne false en cas
    /// d'echec d'envoi, sans jamais lever d'exception (meme pattern que les autres notifiers Zavu).</summary>
    Task<bool> NotifyRelanceAsync(string telephone, decimal montant, CancellationToken cancellationToken);
}
