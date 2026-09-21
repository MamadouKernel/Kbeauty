using KekeBeauty.Application.Auth;
using KekeBeauty.Application.Partner;
using KekeBeauty.Domain.Entities;
using Microsoft.AspNetCore.Mvc;

namespace KekeBeauty.Api.Controllers;

public sealed record RequestOtpRequest(string Telephone, TypeCompte TypeCompte = TypeCompte.Client);
public sealed record VerifyOtpRequest(string Telephone, string Code, TypeCompte TypeCompte = TypeCompte.Client);

[ApiController]
[Route("auth/otp")]
[Microsoft.AspNetCore.RateLimiting.EnableRateLimiting("auth")]
public sealed class AuthController : ControllerBase
{
    private readonly RequestOtpUseCase _requestOtpUseCase;
    private readonly VerifyOtpUseCase _verifyOtpUseCase;
    private readonly LinkCollaborateurCompteUseCase _linkCollaborateurUseCase;
    private readonly KekeBeauty.Api.Auth.PartnerSessionTokenService _partnerTokens;
    private readonly KekeBeauty.Api.Auth.UserSessionTokenService _userTokens;

    public AuthController(RequestOtpUseCase requestOtpUseCase, VerifyOtpUseCase verifyOtpUseCase, LinkCollaborateurCompteUseCase linkCollaborateurUseCase, KekeBeauty.Api.Auth.PartnerSessionTokenService partnerTokens, KekeBeauty.Api.Auth.UserSessionTokenService userTokens)
    {
        _requestOtpUseCase = requestOtpUseCase;
        _verifyOtpUseCase = verifyOtpUseCase;
        _linkCollaborateurUseCase = linkCollaborateurUseCase;
        _partnerTokens = partnerTokens;
        _userTokens = userTokens;
    }

    [HttpPost("request")]
    public async Task<IActionResult> RequestOtp([FromBody] RequestOtpRequest request, CancellationToken cancellationToken)
    {
        var result = await _requestOtpUseCase.ExecuteAsync(request.Telephone, request.TypeCompte, cancellationToken);

        if (result.Success)
        {
            return Accepted(new { status = result.Status });
        }

        if (result.Status == "invalid_phone")
        {
            return BadRequest(new { status = result.Status, message = result.Message });
        }

        if (result.Status == "account_suspended")
        {
            return StatusCode(StatusCodes.Status403Forbidden, new { status = result.Status, message = result.Message });
        }

        return StatusCode(StatusCodes.Status502BadGateway, new { status = result.Status, message = result.Message });
    }

    [HttpPost("verify")]
    public async Task<IActionResult> Verify([FromBody] VerifyOtpRequest request, CancellationToken cancellationToken)
    {
        var result = await _verifyOtpUseCase.ExecuteAsync(request.Telephone, request.TypeCompte, request.Code, cancellationToken);

        if (!result.Success)
        {
            return BadRequest(new { status = result.Status });
        }

        if (request.TypeCompte != TypeCompte.Collaborateur)
        {
            return Ok(new { status = result.Status, idUtilisateur = result.IdUtilisateur, isNewAccount = result.IsNewAccount,
                sessionToken = request.TypeCompte == TypeCompte.Partenaire ? _partnerTokens.Create(result.IdUtilisateur!.Value) : _userTokens.Create(result.IdUtilisateur!.Value, "CLIENT") });
        }

        // Parcours 5 : un compte COLLABORATEUR n'est utile que si le gerant a deja pre-enregistre
        // cette collaboratrice (meme telephone) - sinon on le signale explicitement plutot que de
        // laisser un compte "fantome" sans acces au portail.
        var idCollaborateur = await _linkCollaborateurUseCase.ExecuteAsync(request.Telephone, result.IdUtilisateur!.Value, cancellationToken);
        return Ok(new
        {
            status = idCollaborateur is null ? "compte_non_reference" : result.Status,
            idUtilisateur = result.IdUtilisateur,
            isNewAccount = result.IsNewAccount,
            idCollaborateur,
            sessionToken = idCollaborateur is null ? null : _userTokens.Create(result.IdUtilisateur!.Value, "STAFF"),
        });
    }
}
