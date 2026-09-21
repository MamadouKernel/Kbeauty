using KekeBeauty.Application.Onboarding;
using KekeBeauty.Application.Rdv;
using Microsoft.AspNetCore.Mvc;
namespace KekeBeauty.Api.Controllers;
[ApiController]
public sealed class AvisMediaController(IAvisRepository avis,IFileStorage storage):ControllerBase
{
 [HttpPost("rdv/{id:guid}/avis/photos")][RequestSizeLimit(16_000_000)]
 public async Task<IActionResult> Upload(Guid id,IFormFile photoAvant,IFormFile photoApres,[FromForm]bool partagerPubliquement,CancellationToken ct)
 {
  if(!Request.Headers.TryGetValue("X-Client-Id",out var h)||!Guid.TryParse(h,out var client))return Unauthorized();
  if(photoAvant is null||photoApres is null||photoAvant.Length==0||photoApres.Length==0)return BadRequest(new{status="deux_photos_requises"});
  static string? Ext(IFormFile f){var e=Path.GetExtension(f.FileName).TrimStart('.').ToLowerInvariant();return e is "jpg" or "jpeg" or "png" or "webp"?e:null;}
  var beforeExt=Ext(photoAvant);var afterExt=Ext(photoApres);if(beforeExt is null||afterExt is null)return BadRequest(new{status="format_invalide"});
  await using var before=photoAvant.OpenReadStream();await using var after=photoApres.OpenReadStream();
  var beforePath=await storage.SaveAsync(id,"avis-avant",beforeExt,before,ct);var afterPath=await storage.SaveAsync(id,"avis-apres",afterExt,after,ct);
  var result=await avis.SaveBeforeAfterAsync(id,client,beforePath,afterPath,partagerPubliquement,ct);return result.Saved?Ok(new{status="saved",bonusPoints=result.BonusAwarded?200:0,partagePublic=partagerPubliquement}):NotFound(new{status="avis_introuvable"});
 }
 [HttpGet("media/avis/{id:guid}/{type}")]
 public async Task<IActionResult> Get(Guid id,string type,CancellationToken ct)
 {if(type is not("avant" or "apres"))return NotFound();var path=await avis.GetPhotoPathAsync(id,type,ct);if(path is null)return NotFound();var stream=await storage.OpenAsync(path,ct);return stream is null?NotFound():File(stream,MediaController.ContentType(path),true);}
}