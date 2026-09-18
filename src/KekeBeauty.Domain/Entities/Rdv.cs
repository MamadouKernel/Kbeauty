namespace KekeBeauty.Domain.Entities;

public enum StatutRdv
{
    Demande,
    Confirme,
    Refuse,
    Annule,
    Termine,
}

public class Rdv
{
    public Guid IdRdv { get; set; }
    public DateTimeOffset DateHeureDebut { get; set; }
    public StatutRdv StatutRdv { get; set; }
    public DateTimeOffset DateCreation { get; set; }
    public Guid IdUtilisateurClient { get; set; }
    public Guid IdEtablissement { get; set; }
    public Guid IdPrestation { get; set; }
}
