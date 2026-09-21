namespace KekeBeauty.Web.Models;

public sealed class EtablissementSummary
{
    public Guid IdEtablissement { get; set; }
    public string NomEtablissement { get; set; } = string.Empty;
    public string LibelleCommune { get; set; } = string.Empty;
    public decimal GpsLatitude { get; set; }
    public decimal GpsLongitude { get; set; }
}

public sealed class MediaDto
{
    public Guid IdMedia { get; set; }
    public string TypeMedia { get; set; } = string.Empty;
    public string Url { get; set; } = string.Empty;
    public short OrdreAffichage { get; set; }
}

public sealed class PrestationDto
{
    public Guid IdPrestation { get; set; }
    public string LibellePrestation { get; set; } = string.Empty;
    public decimal Tarif { get; set; }
    public short DureeMinutes { get; set; }
}

public sealed class EtablissementDetail
{
    public Guid IdEtablissement { get; set; }
    public string NomEtablissement { get; set; } = string.Empty;
    public string? Description { get; set; }
    public string NumeroServiceClient { get; set; } = string.Empty;
    public string LienItineraire { get; set; } = string.Empty;
    public List<MediaDto> Medias { get; set; } = [];
    public List<PrestationDto> Prestations { get; set; } = [];
    public decimal GpsLatitude { get; set; }
    public decimal GpsLongitude { get; set; }
    public string ModePaiementService { get; set; } = "ESPECES";
    public bool PaiementWave { get; set; }
    public bool PaiementOrangeMoney { get; set; }
    public bool PaiementMoovMoney { get; set; }
    public bool EstFavori { get; set; }
    public string? Horaires { get; set; }
    public string? MotifRejet { get; set; }
    public string StatutKyc { get; set; } = string.Empty;
}

public sealed class CreneauOccupe
{
    public DateTimeOffset DateHeureDebut { get; set; }
    public DateTimeOffset DateHeureFin { get; set; }
}

public sealed class RequestOtpResponse
{
    public string Status { get; set; } = string.Empty;
    public string? Message { get; set; }
}

public sealed class GoogleLoginResponse
{
    public string Status { get; set; } = string.Empty;
    public Guid? IdUtilisateur { get; set; }
    public bool IsNewAccount { get; set; }
    public string? OnboardingToken { get; set; }
    public string? SessionToken { get; set; }
    public string? DeviceTrustToken { get; set; }
    public string? StepUpTicket { get; set; }
    public bool EmailSent { get; set; }
    public bool WhatsAppSent { get; set; }
}

public sealed class VerifyOtpResponse
{
    public string Status { get; set; } = string.Empty;
    public Guid? IdUtilisateur { get; set; }
    public bool IsNewAccount { get; set; }
    public Guid? IdCollaborateur { get; set; }
    public string? SessionToken { get; set; }
}

public sealed class RequestRdvResponse
{
    public Guid IdRdv { get; set; }
    public string Statut { get; set; } = string.Empty;
}

public sealed class RdvStatutResponse
{
    public Guid IdRdv { get; set; }
    public string Statut { get; set; } = string.Empty;
    public DateTimeOffset DateHeureDebut { get; set; }
}

public sealed class RdvHistoriqueItem
{
    public Guid IdRdv { get; set; }
    public short? RetardMinutes { get; set; }
    public DateTimeOffset? DateSignalementRetard { get; set; }
    public Guid IdEtablissement { get; set; }
    public string NomEtablissement { get; set; } = string.Empty;
    public string LibellePrestation { get; set; } = string.Empty;
    public DateTimeOffset DateHeureDebut { get; set; }
    public string StatutRdv { get; set; } = string.Empty;
    public string? StatutPaiement { get; set; }
    public string ModePaiementService { get; set; } = "ESPECES";
    public bool ADejaAvis { get; set; }
    public Guid? IdCollaborateur { get; set; }
    public decimal TarifPrestation { get; set; }
}

public sealed class AvisItem
{
    public short Note { get; set; }
    public string? Commentaire { get; set; }
    public DateTimeOffset DateCreation { get; set; }
    public Guid IdRdv { get; set; }
    public bool HasPhotoAvant { get; set; }
    public bool HasPhotoApres { get; set; }
}

public sealed class AvisEtablissement
{
    public double? NoteMoyenne { get; set; }
    public int NombreAvis { get; set; }
    public List<AvisItem> Avis { get; set; } = [];
}

public sealed class PaiementRdvInfo
{
    public string StatutPaiement { get; set; } = string.Empty;
    public string? LienPaiement { get; set; }
    public string? ReferenceExterne { get; set; }
}

public sealed class PartenaireStatistiquesDto
{
    public int NombreRdvConfirmes { get; set; }
    public int NombreRdvTermines { get; set; }
    public decimal RevenuEstime { get; set; }
    public string? StatutAbonnement { get; set; }
}

public sealed class CollaborateurDto
{
    public Guid IdCollaborateur { get; set; }
    public string Nom { get; set; } = string.Empty;
    public string? Specialite { get; set; }
    public string? Telephone { get; set; }
    public bool CompteActif { get; set; }
}

public sealed class PlanningItemDto
{
    public Guid IdRdv { get; set; }
    public DateTimeOffset DateHeureDebut { get; set; }
    public string StatutRdv { get; set; } = string.Empty;
    public string LibellePrestation { get; set; } = string.Empty;
    public string NomEtablissement { get; set; } = string.Empty;
    public string NomClient { get; set; } = string.Empty;
    public short DureeMinutes { get; set; }
    public DateTimeOffset? DateDebutReelle { get; set; }
    public DateTimeOffset? DatePause { get; set; }
    public int DureePauseSecondes { get; set; }
    public DateTimeOffset? DateFinReelle { get; set; }
    public short? RetardMinutes { get; set; }
    public DateTimeOffset? DateSignalementRetard { get; set; }
}

public sealed class CommissionItemDto
{
    public Guid IdRdv { get; set; }
    public DateTimeOffset DateHeureDebut { get; set; }
    public decimal Montant { get; set; }
    public string StatutPaiement { get; set; } = string.Empty;
    public string NomClient { get; set; } = string.Empty;
    public string LibellePrestation { get; set; } = string.Empty;
    public decimal TarifPrestation { get; set; }
    public decimal Commission { get; set; }
    public decimal Pourboire { get; set; }
}

public sealed class DemandeRemboursementItem
{
    public Guid IdRdv { get; set; }
    public decimal Montant { get; set; }
    public DateTimeOffset DateTransaction { get; set; }
    public string NomEtablissement { get; set; } = string.Empty;
    public string TelephoneClient { get; set; } = string.Empty;
}

public sealed class LitigeItem
{
    public Guid IdLitige { get; set; }
    public Guid IdRdv { get; set; }
    public string Motif { get; set; } = string.Empty;
    public string StatutLitige { get; set; } = string.Empty;
    public string? Resolution { get; set; }
    public DateTimeOffset DateCreation { get; set; }
    public string NomEtablissement { get; set; } = string.Empty;
    public string TelephoneDeclarant { get; set; } = string.Empty;
}

public sealed class RequestRdvWithPaiementResponse
{
    public Guid IdRdv { get; set; }
    public string Statut { get; set; } = string.Empty;
    public PaiementRdvInfo? Paiement { get; set; }
    public string ModePaiementService { get; set; } = "ESPECES";
}
