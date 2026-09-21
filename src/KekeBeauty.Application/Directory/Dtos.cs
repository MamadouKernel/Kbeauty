namespace KekeBeauty.Application.Directory;

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

public sealed record EtablissementDetail(
    Guid IdEtablissement,
    string NomEtablissement,
    string? Description,
    string NumeroServiceClient,
    string LienItineraire,
    IReadOnlyList<MediaDto> Medias,
    IReadOnlyList<PrestationDto> Prestations,
    decimal GpsLatitude,
    decimal GpsLongitude,
    string ModePaiementService,
    bool PaiementWave,
    bool PaiementOrangeMoney,
    bool PaiementMoovMoney,
    bool EstFavori = false,
    string? Horaires = null,
    string? MotifRejet = null,
    string StatutKyc = "");
