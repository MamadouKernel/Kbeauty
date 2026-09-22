using KekeBeauty.Application.Auth;
using KekeBeauty.Domain.Entities;
using Microsoft.AspNetCore.Mvc;

namespace KekeBeauty.Api.Controllers;

public sealed record RegisterPasswordRequest(string Nom, string Telephone, string Email, string Password, TypeCompte TypeCompte = TypeCompte.Client);
public sealed record VerifyEmailRequest(Guid IdUtilisateur, string Code);
public sealed record LoginPasswordRequest(string Email, string Password, TypeCompte TypeCompte = TypeCompte.Client);
public sealed record ForgotPasswordRequest(string Email, TypeCompte TypeCompte = TypeCompte.Client);
public sealed record ResetPasswordRequest(string Email, string Code, string NewPassword, TypeCompte TypeCompte = TypeCompte.Client);

/// <summary>
/// Auth par email + mot de passe, additive aux flux OTP (AuthController) et Google (GoogleAuthController)
/// deja en place : les trois moyens de connexion produisent le meme type de session token pour un
/// meme Utilisateur.
/// </summary>
[ApiController]
[Route("auth/password")]
[Microsoft.AspNetCore.RateLimiting.EnableRateLimiting("auth")]
public sealed class AuthPasswordController : ControllerBase
{
    private readonly RegisterWithPasswordUseCase _register;
    private readonly VerifyEmailUseCase _verifyEmail;
    private readonly LoginWithPasswordUseCase _login;
    private readonly RequestPasswordResetUseCase _requestReset;
    private readonly ResetPasswordUseCase _resetPassword;
    private readonly KekeBeauty.Api.Auth.PartnerSessionTokenService _partnerTokens;
    private readonly KekeBeauty.Api.Auth.UserSessionTokenService _userTokens;

    public AuthPasswordController(
        RegisterWithPasswordUseCase register, VerifyEmailUseCase verifyEmail, LoginWithPasswordUseCase login,
        RequestPasswordResetUseCase requestReset, ResetPasswordUseCase resetPassword,
        KekeBeauty.Api.Auth.PartnerSessionTokenService partnerTokens, KekeBeauty.Api.Auth.UserSessionTokenService userTokens)
    {
        _register = register;
        _verifyEmail = verifyEmail;
        _login = login;
        _requestReset = requestReset;
        _resetPassword = resetPassword;
        _partnerTokens = partnerTokens;
        _userTokens = userTokens;
    }

    [HttpPost("register")]
    public async Task<IActionResult> Register([FromBody] RegisterPasswordRequest request, CancellationToken cancellationToken)
    {
        var result = await _register.ExecuteAsync(request.Nom, request.Telephone, request.Email, request.Password, request.TypeCompte, cancellationToken);
        if (!result.Success)
        {
            return BadRequest(new { status = result.Status });
        }

        return Ok(new { status = result.Status, idUtilisateur = result.IdUtilisateur, emailSent = result.EmailSent });
    }

    [HttpPost("verify-email")]
    public async Task<IActionResult> VerifyEmail([FromBody] VerifyEmailRequest request, CancellationToken cancellationToken)
    {
        var verified = await _verifyEmail.ExecuteAsync(request.IdUtilisateur, request.Code, cancellationToken);
        return verified ? Ok(new { status = "verified" }) : BadRequest(new { status = "invalid_or_expired_code" });
    }

    [HttpPost("login")]
    public async Task<IActionResult> Login([FromBody] LoginPasswordRequest request, CancellationToken cancellationToken)
    {
        var result = await _login.ExecuteAsync(request.Email, request.Password, request.TypeCompte, cancellationToken);
        if (!result.Success)
        {
            return result.Status == "account_suspended"
                ? StatusCode(StatusCodes.Status403Forbidden, new { status = result.Status })
                : Unauthorized(new { status = result.Status });
        }

        var sessionToken = request.TypeCompte == TypeCompte.Partenaire
            ? _partnerTokens.Create(result.IdUtilisateur!.Value)
            : _userTokens.Create(result.IdUtilisateur!.Value, "CLIENT");
        return Ok(new { status = result.Status, idUtilisateur = result.IdUtilisateur, sessionToken });
    }

    [HttpPost("forgot")]
    public async Task<IActionResult> Forgot([FromBody] ForgotPasswordRequest request, CancellationToken cancellationToken)
    {
        // Reponse identique que l'email existe ou non : evite l'enumeration de comptes.
        await _requestReset.ExecuteAsync(request.Email, request.TypeCompte, cancellationToken);
        return Ok(new { status = "sent_if_exists" });
    }

    [HttpPost("reset")]
    public async Task<IActionResult> Reset([FromBody] ResetPasswordRequest request, CancellationToken cancellationToken)
    {
        var result = await _resetPassword.ExecuteAsync(request.Email, request.TypeCompte, request.Code, request.NewPassword, cancellationToken);
        return result.Success ? Ok(new { status = result.Status }) : BadRequest(new { status = result.Status });
    }
}
