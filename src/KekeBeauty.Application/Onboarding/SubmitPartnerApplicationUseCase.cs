using KekeBeauty.Application.Auth;
using KekeBeauty.Domain.Entities;

namespace KekeBeauty.Application.Onboarding;

public sealed record PartnerApplicationSubmission(
    string Telephone,
    string NomEtablissement,
    decimal GpsLatitude,
    decimal GpsLongitude,
    string NumeroServiceClient,
    string? Horaires,
    Stream? PhotoDevanture,
    string? PhotoDevantureExtension,
    Stream? PieceIdentite,
    string? PieceIdentiteExtension);

public sealed class SubmitPartnerApplicationUseCase
{
    private static readonly string[] AllowedImageExtensions = { "jpg", "jpeg", "png" };

    private readonly IEtablissementRepository _etablissementRepository;
    private readonly IFileStorage _fileStorage;
    private readonly IUtilisateurRepository _utilisateurRepository;

    public SubmitPartnerApplicationUseCase(
        IEtablissementRepository etablissementRepository, IFileStorage fileStorage, IUtilisateurRepository utilisateurRepository)
    {
        _etablissementRepository = etablissementRepository;
        _fileStorage = fileStorage;
        _utilisateurRepository = utilisateurRepository;
    }

    public async Task<SubmitApplicationResult> ExecuteAsync(PartnerApplicationSubmission submission, CancellationToken cancellationToken)
    {
        var validationError = Validate(submission);
        if (validationError is not null)
        {
            return new SubmitApplicationResult(false, "invalid_submission", Message: validationError);
        }

        var (utilisateur, _) = await _utilisateurRepository.FindOrCreateAsync(
            submission.Telephone, TypeCompte.Partenaire, cancellationToken);

        // L'identifiant de l'etablissement est genere ici (pas par la base) afin que les fichiers
        // stockes sous ce meme identifiant correspondent exactement a la ligne creee ensuite.
        var idEtablissement = Guid.NewGuid();

        var photoPath = await _fileStorage.SaveAsync(
            idEtablissement, "devanture", submission.PhotoDevantureExtension!, submission.PhotoDevanture!, cancellationToken);
        var piecePath = await _fileStorage.SaveAsync(
            idEtablissement, "piece-identite", submission.PieceIdentiteExtension!, submission.PieceIdentite!, cancellationToken);

        var idCree = await _etablissementRepository.CreateAsync(
            new NewEtablissement(
                idEtablissement,
                submission.NomEtablissement,
                submission.GpsLatitude,
                submission.GpsLongitude,
                submission.NumeroServiceClient,
                submission.Horaires,
                utilisateur.IdUtilisateur,
                photoPath,
                piecePath),
            cancellationToken);

        return new SubmitApplicationResult(true, "EN_ATTENTE", idCree);
    }

    private static string? Validate(PartnerApplicationSubmission submission)
    {
        if (string.IsNullOrWhiteSpace(submission.Telephone))
        {
            return "Le numero de telephone est obligatoire.";
        }

        if (string.IsNullOrWhiteSpace(submission.NomEtablissement))
        {
            return "Le nom de l'etablissement est obligatoire.";
        }

        if (string.IsNullOrWhiteSpace(submission.NumeroServiceClient))
        {
            return "Le numero de service client est obligatoire.";
        }

        if (submission.PhotoDevanture is null || string.IsNullOrWhiteSpace(submission.PhotoDevantureExtension) ||
            !AllowedImageExtensions.Contains(submission.PhotoDevantureExtension.TrimStart('.').ToLowerInvariant()))
        {
            return "La photo de devanture est obligatoire et doit etre une image (jpg/jpeg/png).";
        }

        if (submission.PieceIdentite is null || string.IsNullOrWhiteSpace(submission.PieceIdentiteExtension) ||
            !AllowedImageExtensions.Contains(submission.PieceIdentiteExtension.TrimStart('.').ToLowerInvariant()))
        {
            return "La piece d'identite est obligatoire et doit etre une image (jpg/jpeg/png).";
        }

        return null;
    }
}
