namespace KekeBeauty.Domain.Entities;

public enum Periodicite
{
    Mensuel,
    Annuel,
}

public enum StatutAbonnement
{
    Actif,
    Impaye,
    Resilie,
}

public class Abonnement
{
    public Guid IdAbonnement { get; set; }
    public Periodicite Periodicite { get; set; }
    public DateOnly DateDebutEngagement { get; set; }
    public decimal Montant { get; set; }
    public StatutAbonnement StatutAbonnement { get; set; }
    public Guid IdEtablissement { get; set; }
}
