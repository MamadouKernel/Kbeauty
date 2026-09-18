namespace KekeBeauty.Web.Models;

public sealed class EtablissementSummary
{
    public Guid IdEtablissement { get; set; }
    public string NomEtablissement { get; set; } = string.Empty;
    public string LibelleCommune { get; set; } = string.Empty;
}

public sealed class MediaDto
{
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

public sealed class VerifyOtpResponse
{
    public string Status { get; set; } = string.Empty;
    public Guid? IdUtilisateur { get; set; }
    public bool IsNewAccount { get; set; }
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
