using KekeBeauty.Application.Onboarding;
using KekeBeauty.Application.Partner;
using Microsoft.AspNetCore.Mvc;
namespace KekeBeauty.Api.Controllers;
[ApiController][Route("media")]
public sealed class MediaController(IPartnerPrestationRepository repository, IEtablissementRepository establishments, IFileStorage storage):ControllerBase
{
 [HttpGet("{id:guid}")] public async Task<IActionResult> Get(Guid id,CancellationToken ct){var path=await repository.GetMediaPathAsync(id,ct);return await Open(path,ct);}
 [HttpGet("devanture/{id:guid}")] public async Task<IActionResult> GetStorefront(Guid id,CancellationToken ct){var path=await establishments.GetFilePathAsync(id,"devanture",ct);return await Open(path,ct);}
 private async Task<IActionResult> Open(string? path,CancellationToken ct){if(path is null)return NotFound();var stream=await storage.OpenAsync(path,ct);return stream is null?NotFound():File(stream,ContentType(path),enableRangeProcessing:true);}
 internal static string ContentType(string path)=>Path.GetExtension(path).ToLowerInvariant() switch{".jpg" or ".jpeg"=>"image/jpeg",".png"=>"image/png",".webp"=>"image/webp",".pdf"=>"application/pdf",_=>"application/octet-stream"};
}