using KekeBeauty.Api.Auth;
using KekeBeauty.Application.Billing;
using Microsoft.AspNetCore.Mvc;

namespace KekeBeauty.Api.Controllers;

public sealed record SubscribeBody(string Periodicite);
public sealed record UpdateTarifBody(decimal Montant);

[ApiController]
public sealed class BillingController : ControllerBase
{
    private static readonly string[] PeriodicitesValides = ["MENSUEL", "ANNUEL"];
    private static readonly string[] StatutsValides = ["ACTIF", "IMPAYE", "RESILIE"];

    private readonly SubscribeUseCase _subscribeUseCase;
    private readonly AdminListAbonnementsUseCase _listUseCase;
    private readonly RelanceUseCase _relanceUseCase;
    private readonly UpdateTarifUseCase _updateTarifUseCase;

    public BillingController(
        SubscribeUseCase subscribeUseCase, AdminListAbonnementsUseCase listUseCase,
        RelanceUseCase relanceUseCase, UpdateTarifUseCase updateTarifUseCase)
    {
        _subscribeUseCase = subscribeUseCase;
        _listUseCase = listUseCase;
        _relanceUseCase = relanceUseCase;
        _updateTarifUseCase = updateTarifUseCase;
    }

    [HttpPost("etablissements/{id:guid}/abonnements")]
    [ServiceFilter(typeof(PartnerOwnershipFilter))]
    public async Task<IActionResult> Subscribe(Guid id, [FromBody] SubscribeBody body, CancellationToken cancellationToken)
    {
        if (!PeriodicitesValides.Contains(body.Periodicite))
        {
            return BadRequest(new { status = "invalid_input" });
        }

        var result = await _subscribeUseCase.ExecuteAsync(id, body.Periodicite, cancellationToken);

        if (!result.Success)
        {
            return result.Status == "payment_initiation_failed"
                ? StatusCode(StatusCodes.Status502BadGateway, new { status = result.Status })
                : Conflict(new { status = result.Status });
        }

        return StatusCode(StatusCodes.Status201Created, new
        {
            idAbonnement = result.IdAbonnement,
            statut = result.StatutAbonnement,
            checkoutUrl = result.CheckoutUrl,
        });
    }

    [HttpGet("admin/abonnements")]
    [ServiceFilter(typeof(AdminApiKeyFilter))]
    public async Task<IActionResult> ListAbonnements([FromQuery] string? statut, CancellationToken cancellationToken)
    {
        if (statut is not null && !StatutsValides.Contains(statut))
        {
            return BadRequest(new { status = "invalid_statut" });
        }

        var abonnements = await _listUseCase.ExecuteAsync(statut, cancellationToken);
        return Ok(abonnements);
    }

    [HttpPost("admin/abonnements/{id:guid}/relance")]
    [ServiceFilter(typeof(AdminApiKeyFilter))]
    public async Task<IActionResult> Relancer(Guid id, CancellationToken cancellationToken)
    {
        var result = await _relanceUseCase.ExecuteAsync(id, cancellationToken);

        if (!result.Success)
        {
            return result.Status == "not_found" ? NotFound() : Conflict(new { status = result.Status });
        }

        return Ok(new { notificationEnvoyee = result.NotificationEnvoyee });
    }

    [HttpGet("admin/tarifs")]
    [ServiceFilter(typeof(AdminApiKeyFilter))]
    public async Task<IActionResult> ListerTarifs(CancellationToken cancellationToken)
    {
        var tarifs = await _updateTarifUseCase.ListerAsync(cancellationToken);
        return Ok(tarifs);
    }

    [HttpPut("admin/tarifs/{periodicite}")]
    [ServiceFilter(typeof(AdminApiKeyFilter))]
    public async Task<IActionResult> UpdateTarif(string periodicite, [FromBody] UpdateTarifBody body, CancellationToken cancellationToken)
    {
        if (!PeriodicitesValides.Contains(periodicite))
        {
            return BadRequest(new { status = "invalid_periodicite" });
        }

        var success = await _updateTarifUseCase.ExecuteAsync(periodicite, body.Montant, cancellationToken);
        if (!success)
        {
            return BadRequest(new { status = "invalid_montant" });
        }

        return Ok(new { periodicite, montant = body.Montant });
    }
}
