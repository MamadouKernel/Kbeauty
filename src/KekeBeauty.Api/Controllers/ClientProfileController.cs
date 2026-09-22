using KekeBeauty.Application.Auth;
using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Mvc;

namespace KekeBeauty.Api.Controllers;
public sealed record UpdateClientProfileRequest(string Nom, string? Email, bool NotificationsRdv, bool NotificationsMarketing, bool ConsentementDonnees);
public sealed record ClientPhoneRequest(string Telephone);
public sealed record ClientPhoneVerifyRequest(string Telephone, string Code);
[ApiController, Route("clients/profil")]
public sealed class ClientProfileController : ControllerBase
{
    private readonly IUtilisateurRepository _users;
    private readonly RequestOtpUseCase _requestOtp;
    private readonly IOtpChallengeRepository _challenges;
    public ClientProfileController(IUtilisateurRepository users, RequestOtpUseCase requestOtp, IOtpChallengeRepository challenges)
    { _users = users; _requestOtp = requestOtp; _challenges = challenges; }
    private bool TryClient(out Guid id) => Guid.TryParse(Request.Headers["X-Client-Id"], out id);
    [HttpGet] public async Task<IActionResult> Get(CancellationToken ct) { if(!TryClient(out var id)) return Unauthorized(); var u=await _users.GetProfileAsync(id,ct); return u is null?NotFound():Ok(new { u.IdUtilisateur,u.Telephone,u.Nom,u.Email,u.NotificationsRdv,u.NotificationsMarketing,u.ConsentementDonnees,pointsFidelite=await _users.GetLoyaltyPointsAsync(id,ct) }); }
    [HttpPut] public async Task<IActionResult> Update([FromBody] UpdateClientProfileRequest body,CancellationToken ct) { if(!TryClient(out var id)) return Unauthorized(); if(string.IsNullOrWhiteSpace(body.Nom)||body.Nom.Trim().Length>100) return BadRequest(new{message="Nom invalide."}); if(body.Email is not null && !System.Net.Mail.MailAddress.TryCreate(body.Email,out _)) return BadRequest(new{message="Email invalide."}); try { return await _users.UpdateProfileAsync(id,body.Nom.Trim(),body.Email?.Trim(),body.NotificationsRdv,body.NotificationsMarketing,body.ConsentementDonnees,ct)?NoContent():NotFound(); } catch(Npgsql.PostgresException e) when(e.SqlState=="23505") { return Conflict(new{message="Cet email est déjà utilisé."}); } }
    [HttpPost("telephone/request")]
    public async Task<IActionResult> RequestTelephone([FromBody] ClientPhoneRequest body, CancellationToken ct)
    {
        if (!TryClient(out _)) return Unauthorized();
        var result = await _requestOtp.ExecuteAsync(body.Telephone, KekeBeauty.Domain.Entities.TypeCompte.Client, ct);
        return result.Success ? Accepted(new { status = result.Status }) : BadRequest(new { status = result.Status, message = result.Message });
    }

    [HttpPost("telephone/verify")]
    public async Task<IActionResult> VerifyTelephone([FromBody] ClientPhoneVerifyRequest body, CancellationToken ct)
    {
        if (!TryClient(out var id)) return Unauthorized();
        var challenge = await _challenges.GetActivePendingAsync(body.Telephone, "CLIENT", ct);
        if (challenge is not null && challenge.Attempts >= OtpChallengePolicy.MaxAttempts) challenge = null;
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(body.Code))).ToLowerInvariant();
        if (challenge is null || challenge.ExpiresAt < DateTimeOffset.UtcNow || !CryptographicOperations.FixedTimeEquals(Encoding.ASCII.GetBytes(challenge.CodeHash), Encoding.ASCII.GetBytes(hash)))
        {
            // Audit securite : compteur d'echecs independant du rate limiting par IP.
            if (challenge is not null) await _challenges.RegisterFailedAttemptAsync(challenge.Id, ct);
            return BadRequest(new { status = "invalid_or_expired_code", message = "Code invalide ou expiré." });
        }
        try
        {
            if (!await _users.UpdateTelephoneAsync(id, body.Telephone, ct)) return NotFound();
            await _challenges.MarkConsumedAsync(challenge.Id, ct);
            return NoContent();
        }
        catch (Npgsql.PostgresException e) when (e.SqlState == "23505")
        { return Conflict(new { status = "phone_already_used", message = "Ce numéro est déjà associé à un autre compte." }); }
    }
    [HttpDelete] public async Task<IActionResult> Delete(CancellationToken ct) { if(!TryClient(out var id)) return Unauthorized(); return await _users.AnonymizeAsync(id,ct)?NoContent():NotFound(); }
}
