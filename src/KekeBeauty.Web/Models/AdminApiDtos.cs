namespace KekeBeauty.Web.Models;

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
    public bool HasPieceIdentite { get; set; }
    public DateTimeOffset DateCreation { get; set; }
}

public sealed class AbonnementResume
{
    public Guid IdAbonnement { get; set; }
    public Guid IdEtablissement { get; set; }
    public string Periodicite { get; set; } = string.Empty;
    public decimal Montant { get; set; }
    public string StatutAbonnement { get; set; } = string.Empty;
}

public sealed class TarifStandard
{
    public string Periodicite { get; set; } = string.Empty;
    public decimal Montant { get; set; }
}
