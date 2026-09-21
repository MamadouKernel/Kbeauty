using KekeBeauty.Application.Onboarding;
using Microsoft.AspNetCore.Mvc;

namespace KekeBeauty.Api.Controllers;

[ApiController]
[Route("partners/applications")]
[Microsoft.AspNetCore.RateLimiting.EnableRateLimiting("onboarding")]
public sealed class PartnersController : ControllerBase
{
    private readonly SubmitPartnerApplicationUseCase _submitUseCase;
    private readonly KekeBeauty.Api.Auth.PartnerSessionTokenService _partnerTokens;

    public PartnersController(SubmitPartnerApplicationUseCase submitUseCase, KekeBeauty.Api.Auth.PartnerSessionTokenService partnerTokens)
    {
        _submitUseCase = submitUseCase;
        _partnerTokens = partnerTokens;
    }

    [HttpPost]
    [RequestSizeLimit(20_000_000)]
    public async Task<IActionResult> Submit(
        [FromForm] string telephone,
        [FromForm] string nomEtablissement,
        [FromForm] decimal gpsLatitude,
        [FromForm] decimal gpsLongitude,
        [FromForm] string numeroServiceClient,
        [FromForm] string? horaires,
        [FromForm] string modePaiementService,
        [FromForm] bool paiementWave,
        [FromForm] bool paiementOrangeMoney,
        [FromForm] bool paiementMoovMoney,
        [FromForm] string? googleOnboardingToken,
        [FromForm] string? categorie,
        [FromForm] string typeDocumentIdentite,
        IFormFile? photoDevanture,
        IFormFile? documentRecto,
        IFormFile? documentVerso,
        CancellationToken cancellationToken)
    {
        if (!ValidateFiles(photoDevanture, documentRecto, documentVerso, out var fileError))
            return BadRequest(new { status = "invalid_file", message = fileError });
        Guid? authenticatedPartnerId = _partnerTokens.TryFromRequest(Request, out var partnerId) ? partnerId : null;
        if (string.IsNullOrWhiteSpace(googleOnboardingToken) && authenticatedPartnerId is null)
            return Unauthorized(new { status = "authentication_required", message = "Validez Google ou le code WhatsApp avant de créer la boutique." });
        var submission = new PartnerApplicationSubmission(
            telephone,
            nomEtablissement,
            gpsLatitude,
            gpsLongitude,
            numeroServiceClient,
            horaires,
            photoDevanture?.OpenReadStream(),
            GetExtension(photoDevanture),
            typeDocumentIdentite.Trim().ToUpperInvariant(),
            documentRecto?.OpenReadStream(),
            GetExtension(documentRecto),
            documentVerso?.OpenReadStream(),
            GetExtension(documentVerso),
            modePaiementService,
            paiementWave,
            paiementOrangeMoney,
            paiementMoovMoney,
            googleOnboardingToken,
            categorie,
            authenticatedPartnerId);

        var result = await _submitUseCase.ExecuteAsync(submission, cancellationToken);

        if (!result.Success)
        {
            return BadRequest(new { status = result.Status, message = result.Message });
        }

        return StatusCode(StatusCodes.Status201Created, new { idEtablissement = result.IdEtablissement, statut = result.Status });
    }


    private static bool ValidateFiles(IFormFile? storefront, IFormFile? front, IFormFile? back, out string? error)
    {
        error = null;
        foreach (var file in new[] { storefront, front, back }.Where(file => file is not null))
        {
            if (file!.Length <= 0 || file.Length > 8 * 1024 * 1024) { error = "Chaque fichier doit contenir entre 1 octet et 8 Mo."; return false; }
            var extension = Path.GetExtension(file.FileName).TrimStart('.').ToLowerInvariant();
            if (!TryDetectFileType(file, out var detected) || extension != detected && !(detected == "jpg" && extension == "jpeg"))
            { error = "Le contenu du fichier ne correspond pas à son extension."; return false; }
            var expectedContentType = detected switch { "jpg" => "image/jpeg", "png" => "image/png", "webp" => "image/webp", "pdf" => "application/pdf", _ => "" };
            if (!string.Equals(file.ContentType, expectedContentType, StringComparison.OrdinalIgnoreCase))
            { error = "Le type MIME du fichier est invalide."; return false; }
        }
        if (storefront is not null && Path.GetExtension(storefront.FileName).Equals(".pdf", StringComparison.OrdinalIgnoreCase))
        { error = "La devanture doit être une image."; return false; }
        return true;
    }

    private static bool TryDetectFileType(IFormFile file, out string type)
    {
        type = "";
        Span<byte> header = stackalloc byte[12];
        using var stream = file.OpenReadStream();
        var read = stream.Read(header);
        if (read >= 3 && header[0] == 0xFF && header[1] == 0xD8 && header[2] == 0xFF) { type = "jpg"; return true; }
        if (read >= 8 && header[..8].SequenceEqual(new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A })) { type = "png"; return true; }
        if (read >= 12 && header[..4].SequenceEqual("RIFF"u8) && header[8..12].SequenceEqual("WEBP"u8)) { type = "webp"; return true; }
        if (read >= 5 && header[..5].SequenceEqual("%PDF-"u8)) { type = "pdf"; return true; }
        return false;
    }

    private static string? GetExtension(IFormFile? file) =>
        file is null ? null : Path.GetExtension(file.FileName).TrimStart('.');
}
