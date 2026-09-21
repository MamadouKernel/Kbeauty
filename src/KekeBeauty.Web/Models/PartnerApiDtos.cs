namespace KekeBeauty.Web.Models;

public sealed class EtablissementGere
{
    public Guid IdEtablissement { get; set; }
    public string NomEtablissement { get; set; } = string.Empty;
    public string StatutKyc { get; set; } = string.Empty;
    public bool EstSuspendu { get; set; }
}

public sealed class RdvPartenaire
{
    public Guid IdRdv { get; set; }
    public string StatutRdv { get; set; } = string.Empty;
    public DateTimeOffset DateHeureDebut { get; set; }
    public string LibellePrestation { get; set; } = string.Empty;
    public string TelephoneClient { get; set; } = string.Empty;
    public string NomClient { get; set; } = string.Empty;
    public short DureeMinutes { get; set; }
    public decimal TarifPrestation { get; set; }
    public string OrigineRdv { get; set; } = "EN_LIGNE";
    public Guid? IdCollaborateur { get; set; }
    public short? RetardMinutes { get; set; }
    public DateTimeOffset? DateSignalementRetard { get; set; }
}

public sealed class PlanUsageDto
{
    public string Formule { get; set; } = "FREE";
    public int PrestationsUtilisees { get; set; }
    public int? LimitePrestations { get; set; }
    public int RendezVousMoisUtilises { get; set; }
    public int? LimiteRdvMensuels { get; set; }
    public bool PaiementMobile { get; set; }
    public bool GestionEquipe { get; set; }
    public bool StatistiquesAvancees { get; set; }
}

public sealed class TarifAbonnementDto
{
    public string Periodicite { get; set; } = string.Empty;
    public decimal Montant { get; set; }
}

public sealed class IndisponibiliteDto
{
    public Guid IdIndisponibilite { get; set; }
    public DateTimeOffset DateDebut { get; set; }
    public DateTimeOffset DateFin { get; set; }
    public string? Motif { get; set; }
}
public sealed class PartnerProfileDto { public Guid IdUtilisateur {get;set;} public string Telephone {get;set;}=""; public string Nom {get;set;}=""; public string? Email {get;set;} public bool HasPhotoProfil {get;set;} }
public sealed class AbonnementHistoriqueDto { public Guid IdAbonnement {get;set;} public string Periodicite {get;set;}=""; public decimal Montant {get;set;} public string StatutAbonnement {get;set;}=""; public DateOnly DateDebutEngagement {get;set;} }

public sealed class QrVerificationDto { public Guid IdRdv {get;set;} public string Statut {get;set;}=""; public DateTimeOffset DateHeureDebut {get;set;} public string LibellePrestation {get;set;}=""; public string NomClient {get;set;}=""; public decimal AcomptePaye {get;set;} public string Reference {get;set;}=""; public bool Verifie {get;set;} }

public sealed class PrestationItem
{
    public Guid IdPrestation { get; set; }
    public string Libelle { get; set; } = string.Empty;
    public decimal Tarif { get; set; }
    public short DureeMinutes { get; set; }
}

public sealed class CollaborateurItem
{
    public Guid IdCollaborateur { get; set; }
    public string Nom { get; set; } = string.Empty;
    public string? Specialite { get; set; }
}