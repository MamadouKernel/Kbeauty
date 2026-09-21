namespace KekeBeauty.Application.Onboarding;

public sealed record SubmitApplicationResult(bool Success, string Status, Guid? IdEtablissement = null, string? Message = null);

// Classes (pas des record positionnels) : materialisees directement par Dapper depuis les requetes
// SQL de EtablissementRepository. Meme correction que OtpChallenge dans la feature 003 (voir
// specs/003-auth-client/tasks.md, T018) - Dapper ne parvient pas a materialiser un record ici.
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

public sealed record ApplicationDecisionResult(bool Success, string Status, string? Message = null);
