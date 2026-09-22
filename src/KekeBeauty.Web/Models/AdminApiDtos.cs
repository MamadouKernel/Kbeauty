namespace KekeBeauty.Web.Models;

public sealed class AdminStatistiquesGlobalesDto
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

public sealed class ApplicationSummary
{
    public Guid IdEtablissement { get; set; }
    public string NomEtablissement { get; set; } = string.Empty;
    public DateTimeOffset DateCreation { get; set; }
    public string StatutKyc { get; set; } = string.Empty;
}

public sealed class ApplicationDetail
{
    public Guid IdEtablissement { get; set; }
    public string NomEtablissement { get; set; } = string.Empty;
    public decimal GpsLatitude { get; set; }
    public decimal GpsLongitude { get; set; }
    public string NumeroServiceClient { get; set; } = string.Empty;
    public string StatutKyc { get; set; } = string.Empty;
    public bool HasPhotoDevanture { get; set; }
    public string? TypeDocumentIdentite { get; set; }
    public bool HasDocumentRecto { get; set; }
    public bool HasDocumentVerso { get; set; }
    public DateTimeOffset DateCreation { get; set; }
}

public sealed class AbonnementResume
{
    public Guid IdAbonnement { get; set; }
    public Guid IdEtablissement { get; set; }
    public string Formule { get; set; } = string.Empty;
    public string Periodicite { get; set; } = string.Empty;
    public decimal Montant { get; set; }
    public string StatutAbonnement { get; set; } = string.Empty;
}

public sealed class ConfigurationFormuleDto
{
    public string Formule { get; set; } = string.Empty;
    public string Libelle { get; set; } = string.Empty;
    public bool EstActif { get; set; } = true;
    public bool EstDefaut { get; set; }
    public int OrdreAffichage { get; set; }
    public decimal? TarifMensuel { get; set; }
    public decimal? TarifAnnuel { get; set; }
    public int? LimitePrestations { get; set; }
    public int? LimiteRdvMensuels { get; set; }
    public bool PaiementMobile { get; set; }
    public bool GestionEquipe { get; set; }
    public bool StatistiquesAvancees { get; set; }
    public List<string> Avantages { get; set; } = [];
    public int? PromoPourcentage { get; set; }
    public DateTimeOffset? PromoFin { get; set; }
    public bool EstPromoActive { get; set; }
    public decimal? TarifMensuelEffectif { get; set; }
    public decimal? TarifAnnuelEffectif { get; set; }

    public string AvantagesTexte
    {
        get => string.Join('\n', Avantages);
        set => Avantages = value.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList();
    }

    public DateTime? PromoFinLocale
    {
        get => PromoFin?.LocalDateTime;
        set => PromoFin = value is null ? null : new DateTimeOffset(DateTime.SpecifyKind(value.Value, DateTimeKind.Local));
    }
}

public sealed class AdminUserItemDto { public Guid IdUtilisateur {get;set;} public string Nom {get;set;}=""; public string Telephone {get;set;}=""; public string? Email {get;set;} public string TypeCompte {get;set;}=""; public bool EstSuspendu {get;set;} public DateTimeOffset DateCreation {get;set;} }
public sealed class AdminShopItemDto { public Guid IdEtablissement {get;set;} public string NomEtablissement {get;set;}=""; public string Gerant {get;set;}=""; public string Telephone {get;set;}=""; public string StatutKyc {get;set;}=""; public bool EstSuspendu {get;set;} public DateTimeOffset DateCreation {get;set;} }
public sealed class AdminAuditItemDto { public Guid IdJournal {get;set;} public string NomAdmin {get;set;}=""; public string Action {get;set;}=""; public string? TypeCible {get;set;} public Guid? IdCible {get;set;} public string? Details {get;set;} public DateTimeOffset DateAction {get;set;} }
public sealed class AdminAccountItemDto { public Guid IdAdmin {get;set;} public string Nom {get;set;}=""; public string Email {get;set;}=""; public string Role {get;set;}=""; public bool Actif {get;set;} public DateTimeOffset DateCreation {get;set;} public DateTimeOffset? DerniereConnexion {get;set;} }
