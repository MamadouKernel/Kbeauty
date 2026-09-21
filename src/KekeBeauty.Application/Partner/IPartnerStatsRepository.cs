namespace KekeBeauty.Application.Partner;

public sealed class PartnerStatistiques
{
    public int NombreRdvConfirmes { get; set; }
    public int NombreRdvTermines { get; set; }
    public decimal RevenuEstime { get; set; }
    public string? StatutAbonnement { get; set; }
}

/// <summary>
/// Statistiques en lecture seule pour l'espace partenaire (ecran "Revenus & Statistiques Pro").
/// Aucune nouvelle entite metier : agrege rdv/prestation/abonnement deja modelises.
/// </summary>
public interface IPartnerStatsRepository
{
    Task<PartnerStatistiques> GetStatistiquesAsync(Guid idEtablissement, CancellationToken cancellationToken);
}
