namespace KekeBeauty.Application.Admin;

public sealed class AdminStatistiquesGlobales
{
    public int EtablissementsValides { get; set; }
    public int EtablissementsEnAttente { get; set; }
    public int EtablissementsRejetes { get; set; }
    public int RdvDemande { get; set; }
    public int RdvConfirme { get; set; }
    public int RdvTermine { get; set; }
    public int RdvRefuseOuAnnule { get; set; }
    public decimal RevenuEstimeTotal { get; set; }
    public int AbonnementsActifs { get; set; }
    public int AbonnementsImpayes { get; set; }
    public int NombreAvis { get; set; }
    public double? NoteMoyenneGlobale { get; set; }
    public int NombreClients { get; set; }
}

/// <summary>
/// Agregations en lecture seule pour le tableau de bord Super-Admin (feature 017). Toutes les
/// valeurs viennent de requetes SQL reelles (FR-002) - jamais de constante.
/// </summary>
public interface IAdminStatsRepository
{
    Task<AdminStatistiquesGlobales> GetStatistiquesGlobalesAsync(CancellationToken cancellationToken);
}
