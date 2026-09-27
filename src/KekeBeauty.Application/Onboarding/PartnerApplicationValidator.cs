namespace KekeBeauty.Application.Onboarding;

public static class PartnerApplicationValidator
{
    private static readonly HashSet<string> ImageExtensions =
        new(StringComparer.OrdinalIgnoreCase) { "jpg", "jpeg", "png" };

    private static readonly HashSet<string> IdentityExtensions =
        new(StringComparer.OrdinalIgnoreCase) { "jpg", "jpeg", "png", "pdf" };

    public static string? Validate(PartnerApplicationSubmission submission)
    {
        if (string.IsNullOrWhiteSpace(submission.Telephone))
            return "Le numero de telephone est obligatoire.";
        if (string.IsNullOrWhiteSpace(submission.NomEtablissement))
            return "Le nom de l'etablissement est obligatoire.";
        if (string.IsNullOrWhiteSpace(submission.NumeroServiceClient))
            return "Le numero de service client est obligatoire.";
        if (!submission.ConsentementRgpd)
            return "Votre accord sur le traitement des données du dossier est nécessaire.";

        var paymentMode = Normalize(submission.ModePaiementService);
        if (paymentMode is not ("ESPECES" or "EN_LIGNE" or "MIXTE"))
            return "Le mode de paiement des prestations est invalide.";
        if (paymentMode != "ESPECES" && !HasMobilePaymentOperator(submission))
            return "Selectionnez au moins un operateur de paiement mobile.";

        if (submission.PhotoDevanture is null || !HasAllowedExtension(submission.PhotoDevantureExtension, ImageExtensions))
            return "La photo de devanture est obligatoire et doit etre une image (jpg/jpeg/png).";

        var documentType = Normalize(submission.TypeDocumentIdentite);
        if (documentType is not ("CNI" or "PASSEPORT"))
            return "Selectionnez une carte nationale d'identite ou un passeport.";
        if (submission.DocumentRecto is null || !HasAllowedExtension(submission.DocumentRectoExtension, IdentityExtensions))
            return documentType == "PASSEPORT"
                ? "La page d'identification du passeport est obligatoire."
                : "Le recto de la carte d'identite est obligatoire.";
        if (documentType == "CNI" &&
            (submission.DocumentVerso is null || !HasAllowedExtension(submission.DocumentVersoExtension, IdentityExtensions)))
            return "Le verso de la carte d'identite est obligatoire.";

        return null;
    }

    private static bool HasMobilePaymentOperator(PartnerApplicationSubmission submission) =>
        submission.PaiementWave || submission.PaiementOrangeMoney || submission.PaiementMoovMoney;

    private static bool HasAllowedExtension(string? extension, HashSet<string> allowed) =>
        !string.IsNullOrWhiteSpace(extension) && allowed.Contains(extension.TrimStart('.'));

    private static string Normalize(string? value) => value?.Trim().ToUpperInvariant() ?? string.Empty;
}
