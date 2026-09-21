using KekeBeauty.Application.Notifications;
using Microsoft.AspNetCore.Mvc;

namespace KekeBeauty.Api.Controllers;

public sealed record SubscribePushBody(string Endpoint, string P256dh, string Auth);
public sealed record UnsubscribePushBody(string Endpoint);

[ApiController]
[Route("push")]
public sealed class PushController : ControllerBase
{
    private readonly IPushSubscriptionRepository _repository;
    private readonly IConfiguration _configuration;

    public PushController(IPushSubscriptionRepository repository, IConfiguration configuration)
    {
        _repository = repository;
        _configuration = configuration;
    }

    // Cle publique VAPID : donnee non sensible, necessaire cote navigateur pour PushManager.subscribe.
    [HttpGet("vapid-public-key")]
    public IActionResult GetVapidPublicKey()
    {
        var publicKey = _configuration["Push:VapidPublicKey"];
        return string.IsNullOrWhiteSpace(publicKey)
            ? StatusCode(StatusCodes.Status503ServiceUnavailable, new { status = "not_configured" })
            : Ok(new { publicKey });
    }

    [HttpPost("subscribe")]
    public async Task<IActionResult> Subscribe([FromBody] SubscribePushBody body, CancellationToken cancellationToken)
    {
        if (!Request.Headers.TryGetValue("X-Client-Id", out var header) || !Guid.TryParse(header, out var idClient))
        {
            return Unauthorized(new { status = "unauthorized" });
        }

        await _repository.EnregistrerAsync(idClient, body.Endpoint, body.P256dh, body.Auth, cancellationToken);
        return Ok(new { status = "subscribed" });
    }

    [HttpPost("unsubscribe")]
    public async Task<IActionResult> Unsubscribe([FromBody] UnsubscribePushBody body, CancellationToken cancellationToken)
    {
        await _repository.SupprimerAsync(body.Endpoint, cancellationToken);
        return Ok(new { status = "unsubscribed" });
    }
}
