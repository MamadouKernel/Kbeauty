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
    string TypeDocumentIdentite,
    Stream? DocumentRecto,
    string? DocumentRectoExtension,
    Stream? DocumentVerso,
    string? DocumentVersoExtension,
    string ModePaiementService,
    bool PaiementWave,
    bool PaiementOrangeMoney,
    bool PaiementMoovMoney,
    string? GoogleOnboardingToken = null,
    string? Categorie = null,
    Guid? AuthenticatedPartnerId = null,
    string? AdresseTexte = null,
    bool ConsentementRgpd = false,
    string VersionRgpd = "2026-09");

public sealed class SubmitPartnerApplicationUseCase
{
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
        var validationError = PartnerApplicationValidator.Validate(submission);
        if (validationError is not null)
        {
            return new SubmitApplicationResult(false, "invalid_submission", Message: validationError);
        }

        Guid idUtilisateurGerant;
        if (!string.IsNullOrWhiteSpace(submission.GoogleOnboardingToken))
        {
            var tokenHash = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(
                System.Text.Encoding.UTF8.GetBytes(submission.GoogleOnboardingToken)));
            var resolved = await _utilisateurRepository.ConsumePartnerOnboardingTokenAsync(tokenHash, submission.Telephone, cancellationToken);
            if (resolved is null)
                return new SubmitApplicationResult(false, "google_session_expired", Message: "La validation Google a expire. Recommencez la connexion.");
            idUtilisateurGerant = resolved.Value;
        }
        else
        {
            if (submission.AuthenticatedPartnerId is null)
                return new SubmitApplicationResult(false, "authentication_required", Message: "Validez votre compte Google ou le code WhatsApp avant de créer la boutique.");
            var utilisateur = await _utilisateurRepository.GetProfileAsync(submission.AuthenticatedPartnerId.Value, cancellationToken);
            if (utilisateur is null || utilisateur.TypeCompte != TypeCompte.Partenaire || NormalizePhone(utilisateur.Telephone) != NormalizePhone(submission.Telephone))
                return new SubmitApplicationResult(false, "identity_mismatch", Message: "Le téléphone du dossier ne correspond pas au compte partenaire vérifié.");
            idUtilisateurGerant = utilisateur.IdUtilisateur;
        }

        // L'identifiant de l'etablissement est genere ici (pas par la base) afin que les fichiers
        // stockes sous ce meme identifiant correspondent exactement a la ligne creee ensuite.
        var idEtablissement = Guid.NewGuid();

        var photoPath = await _fileStorage.SaveAsync(
            idEtablissement, "devanture", submission.PhotoDevantureExtension!, submission.PhotoDevanture!, cancellationToken);
        var rectoPath = await _fileStorage.SaveAsync(
            idEtablissement, "document-recto", submission.DocumentRectoExtension!, submission.DocumentRecto!, cancellationToken);
        string? versoPath = null;
        if (submission.TypeDocumentIdentite == "CNI")
        {
            versoPath = await _fileStorage.SaveAsync(
                idEtablissement, "document-verso", submission.DocumentVersoExtension!, submission.DocumentVerso!, cancellationToken);
        }

        Guid idCree;
        try
        {
            idCree = await _etablissementRepository.CreateAsync(
            new NewEtablissement(
                idEtablissement,
                submission.NomEtablissement,
                submission.GpsLatitude,
                submission.GpsLongitude,
                submission.NumeroServiceClient,
                submission.Horaires,
                idUtilisateurGerant,
                photoPath,
                submission.TypeDocumentIdentite,
                rectoPath,
                versoPath,
                submission.ModePaiementService.Trim().ToUpperInvariant(),
                submission.PaiementWave,
                submission.PaiementOrangeMoney,
                submission.PaiementMoovMoney,
                submission.Categorie,
                submission.AdresseTexte,
                submission.ConsentementRgpd,
                submission.VersionRgpd),
            cancellationToken);
        }
        catch
        {
            await _fileStorage.DeleteApplicationFilesAsync(idEtablissement, CancellationToken.None);
            throw;
        }

        if (!string.IsNullOrWhiteSpace(submission.GoogleOnboardingToken))
        {
            await _utilisateurRepository.DeletePartnerOnboardingTokenAsync(idUtilisateurGerant, cancellationToken);
        }

        return new SubmitApplicationResult(true, "EN_ATTENTE", idCree);
    }

    private static string NormalizePhone(string value) => new(value.Where(char.IsDigit).ToArray());

}
