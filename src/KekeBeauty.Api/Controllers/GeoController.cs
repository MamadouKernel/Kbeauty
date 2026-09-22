using KekeBeauty.Application.Geo;
using Microsoft.AspNetCore.Mvc;

namespace KekeBeauty.Api.Controllers;

[ApiController]
[Route("geo")]
[Microsoft.AspNetCore.RateLimiting.EnableRateLimiting("geo")]
public sealed class GeoController : ControllerBase
{
    private readonly IGeocodingService _geocoding;

    public GeoController(IGeocodingService geocoding)
    {
        _geocoding = geocoding;
    }

    [HttpGet("search")]
    public async Task<IActionResult> Search([FromQuery] string? q, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(q))
        {
            return Ok(Array.Empty<GeoSuggestion>());
        }

        return Ok(await _geocoding.SearchAsync(q, cancellationToken));
    }

    [HttpGet("reverse")]
    public async Task<IActionResult> Reverse([FromQuery] decimal lat, [FromQuery] decimal lng, CancellationToken cancellationToken)
    {
        var address = await _geocoding.ReverseAsync(lat, lng, cancellationToken);
        return Ok(new { address });
    }
}
