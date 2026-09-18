namespace KekeBeauty.Application.Onboarding;

public interface IPartnerNotifier
{
    /// <summary>Notifie le gerant du rejet de son dossier (FR-009). Retourne false en cas d'echec d'envoi.</summary>
    Task<bool> NotifyRejectionAsync(string telephone, string nomEtablissement, CancellationToken cancellationToken);
}
