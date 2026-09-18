namespace KekeBeauty.Application.Onboarding;

public sealed record NewEtablissement(
    Guid IdEtablissement,
    string NomEtablissement,
    decimal GpsLatitude,
    decimal GpsLongitude,
    string NumeroServiceClient,
    string? Horaires,
    Guid IdUtilisateurGerant,
    string? UrlPhotoDevanture,
    string? UrlPieceIdentite);

public interface IEtablissementRepository
{
    Task<Guid> CreateAsync(NewEtablissement etablissement, CancellationToken cancellationToken);

    Task<IReadOnlyList<ApplicationSummary>> ListByStatutAsync(string statutKyc, CancellationToken cancellationToken);

    Task<ApplicationDetail?> GetByIdAsync(Guid idEtablissement, CancellationToken cancellationToken);

    Task UpdateStatutAsync(Guid idEtablissement, string statutKyc, CancellationToken cancellationToken);

    Task<string?> GetFilePathAsync(Guid idEtablissement, string fileType, CancellationToken cancellationToken);

    Task<string?> GetGerantTelephoneAsync(Guid idEtablissement, CancellationToken cancellationToken);
}
