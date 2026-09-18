using KekeBeauty.Application.Onboarding;
using Microsoft.AspNetCore.Mvc;

namespace KekeBeauty.Api.Controllers;

[ApiController]
[Route("partners/applications")]
public sealed class PartnersController : ControllerBase
{
    private readonly SubmitPartnerApplicationUseCase _submitUseCase;

    public PartnersController(SubmitPartnerApplicationUseCase submitUseCase)
    {
        _submitUseCase = submitUseCase;
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
        IFormFile? photoDevanture,
        IFormFile? pieceIdentite,
        CancellationToken cancellationToken)
    {
        var submission = new PartnerApplicationSubmission(
            telephone,
            nomEtablissement,
            gpsLatitude,
            gpsLongitude,
            numeroServiceClient,
            horaires,
            photoDevanture?.OpenReadStream(),
            GetExtension(photoDevanture),
            pieceIdentite?.OpenReadStream(),
            GetExtension(pieceIdentite));

        var result = await _submitUseCase.ExecuteAsync(submission, cancellationToken);

        if (!result.Success)
        {
            return BadRequest(new { status = result.Status, message = result.Message });
        }

        return StatusCode(StatusCodes.Status201Created, new { idEtablissement = result.IdEtablissement, statut = result.Status });
    }

    private static string? GetExtension(IFormFile? file) =>
        file is null ? null : Path.GetExtension(file.FileName).TrimStart('.');
}
