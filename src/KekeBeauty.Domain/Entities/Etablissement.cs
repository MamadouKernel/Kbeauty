namespace KekeBeauty.Domain.Entities;

public enum StatutKyc
{
    EnAttente,
    Valide,
    Rejete,
}

public class Etablissement
{
    public Guid IdEtablissement { get; set; }
    public string NomEtablissement { get; set; } = string.Empty;
    public string? Description { get; set; }
    public decimal GpsLatitude { get; set; }
    public decimal GpsLongitude { get; set; }
    public string? Horaires { get; set; }
    public string NumeroServiceClient { get; set; } = string.Empty;
    public StatutKyc StatutKyc { get; set; }
    public string? UrlPieceIdentite { get; set; }
    public string? UrlPhotoDevanture { get; set; }
    public Guid IdUtilisateurGerant { get; set; }
    public Guid IdCommune { get; set; }
    public DateTimeOffset DateCreation { get; set; }
}
