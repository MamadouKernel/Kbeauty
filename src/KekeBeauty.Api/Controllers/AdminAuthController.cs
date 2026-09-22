using System.Security.Cryptography;
using KekeBeauty.Api.Auth;
using KekeBeauty.Application.Admin;
using Microsoft.AspNetCore.Mvc;
namespace KekeBeauty.Api.Controllers;
[ApiController][Route("admin/auth")]
[Microsoft.AspNetCore.RateLimiting.EnableRateLimiting("auth")]
public sealed class AdminAuthController(IAdminManagementRepository repo,AdminSessionTokenService tokens,IConfiguration config,ILogger<AdminAuthController> logger):ControllerBase {
 public sealed record LoginBody(string Email,string MotDePasse);
 public sealed record ChangePasswordBody(string MotDePasseActuel,string NouveauMotDePasse);
 [HttpPost("login")] public async Task<IActionResult> Login(LoginBody body,CancellationToken ct){await EnsureBootstrapAsync(ct);var a=await repo.FindByEmailAsync(body.Email,ct);if(a is null||!a.Actif||!Verify(body.MotDePasse,a.MotDePasseHash))return Unauthorized(new{status="identifiants_invalides"});await repo.TouchLoginAsync(a.IdAdmin,ct);await repo.AuditAsync(a.IdAdmin,a.Nom,"CONNEXION",null,null,null,HttpContext.Connection.RemoteIpAddress?.ToString(),ct);return Ok(new{token=tokens.Create(new(a.IdAdmin,a.Nom,a.Role)),a.Nom,a.Email,a.Role});}
 [HttpPost("password")][ServiceFilter(typeof(AdminApiKeyFilter))]
 public async Task<IActionResult> ChangePassword(ChangePasswordBody body,CancellationToken ct)
 {
  var identity=(AdminIdentity)HttpContext.Items["Admin"]!;
  var account=await repo.FindByIdAsync(identity.Id,ct);
  if(account is null||!Verify(body.MotDePasseActuel,account.MotDePasseHash))return Unauthorized(new{status="mot_de_passe_actuel_invalide"});
  if(!AdminPasswordPolicy.IsValid(body.NouveauMotDePasse))return BadRequest(new{status="mot_de_passe_faible",message="12 caractères minimum avec majuscule, minuscule, chiffre et symbole."});
  if(Verify(body.NouveauMotDePasse,account.MotDePasseHash))return Conflict(new{status="mot_de_passe_identique"});
  await repo.UpdateAdminPasswordAsync(identity.Id,Hash(body.NouveauMotDePasse),ct);
  await repo.AuditAsync(identity.Id,identity.Name,"CHANGEMENT_MOT_DE_PASSE","ADMIN",identity.Id,null,HttpContext.Connection.RemoteIpAddress?.ToString(),ct);
  return NoContent();
 }
 [HttpGet("me")][ServiceFilter(typeof(AdminApiKeyFilter))] public IActionResult Me(){var a=(AdminIdentity)HttpContext.Items["Admin"]!;return Ok(new{a.Id,a.Name,a.Role});}
 private async Task EnsureBootstrapAsync(CancellationToken ct){if(await repo.CountAdminsAsync(ct)>0)return;var email=config["Admin:BootstrapEmail"]??"admin@kekebeauty.ci";var pass=config["Admin:BootstrapPassword"]??config["Admin:ApiKey"];if(AdminPasswordPolicy.IsValid(pass)){await repo.CreateAdminAsync("Administrateur Keke Beauty",email,Hash(pass!),"SUPER_ADMIN",ct);}else{logger.LogWarning("Bootstrap admin impossible : Admin:BootstrapPassword/Admin:ApiKey absent ou trop faible (12+ caracteres, majuscule/minuscule/chiffre/symbole requis). Aucun compte SUPER_ADMIN n'a ete cree.");}}
 public static string Hash(string password){var salt=RandomNumberGenerator.GetBytes(16);var hash=Rfc2898DeriveBytes.Pbkdf2(password,salt,150000,HashAlgorithmName.SHA256,32);return $"pbkdf2$150000${Convert.ToBase64String(salt)}${Convert.ToBase64String(hash)}";}
 public static bool Verify(string password,string encoded){try{var p=encoded.Split('$');var salt=Convert.FromBase64String(p[2]);var expected=Convert.FromBase64String(p[3]);var actual=Rfc2898DeriveBytes.Pbkdf2(password,salt,int.Parse(p[1]),HashAlgorithmName.SHA256,expected.Length);return CryptographicOperations.FixedTimeEquals(actual,expected);}catch{return false;}}
}
