using KekeBeauty.Application.Auth;
using KekeBeauty.Application.Onboarding;
using Microsoft.AspNetCore.Mvc;
namespace KekeBeauty.Api.Controllers;
public sealed record UpdatePartnerProfileRequest(string Nom,string? Email);
[ApiController][Route("partenaire/profil")]
public sealed class PartnerProfileController(IUtilisateurRepository users,KekeBeauty.Api.Auth.PartnerSessionTokenService tokens,IFileStorage storage):ControllerBase
{
 bool TryId(out Guid id)=>tokens.TryFromRequest(Request,out id);
 [HttpGet] public async Task<IActionResult> Get(CancellationToken ct){if(!TryId(out var id))return Unauthorized();var u=await users.GetProfileAsync(id,ct);return u is null?NotFound():Ok(new{u.IdUtilisateur,u.Telephone,u.Nom,u.Email,hasPhotoProfil=(await users.GetPartnerPhotoPathAsync(id,ct)) is not null});}
 [HttpPut] public async Task<IActionResult> Put([FromBody]UpdatePartnerProfileRequest body,CancellationToken ct){if(!TryId(out var id))return Unauthorized();if(string.IsNullOrWhiteSpace(body.Nom))return BadRequest(new{status="nom_requis"});return await users.UpdateProfileAsync(id,body.Nom.Trim(),body.Email?.Trim(),true,false,true,ct)?NoContent():NotFound();}
 [HttpPost("photo")][RequestSizeLimit(8_000_000)] public async Task<IActionResult> Photo(IFormFile photo,CancellationToken ct){if(!TryId(out var id))return Unauthorized();if(photo is null||photo.Length==0)return BadRequest(new{status="photo_requise"});var ext=Path.GetExtension(photo.FileName).TrimStart('.').ToLowerInvariant();if(ext is not("jpg" or "jpeg" or "png" or "webp"))return BadRequest(new{status="format_invalide"});await using var input=photo.OpenReadStream();var path=await storage.SaveAsync(id,"profil-partenaire",ext,input,ct);return await users.UpdatePartnerPhotoAsync(id,path,ct)?Ok(new{status="updated"}):NotFound();}
 [HttpGet("photo")] public async Task<IActionResult> Photo(CancellationToken ct){if(!TryId(out var id))return Unauthorized();var path=await users.GetPartnerPhotoPathAsync(id,ct);if(path is null)return NotFound();var stream=await storage.OpenAsync(path,ct);return stream is null?NotFound():File(stream,MediaController.ContentType(path));}
}