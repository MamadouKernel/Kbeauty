using KekeBeauty.Api.Auth;
using KekeBeauty.Application.Billing;
using Microsoft.AspNetCore.Mvc;

namespace KekeBeauty.Api.Controllers;

public sealed record SubscribeBody(string Periodicite);
public sealed record UpdateTarifBody(decimal Montant);
public sealed record UpdateFormuleBody(int? LimitePrestations, int? LimiteRdvMensuels, bool PaiementMobile, bool GestionEquipe, bool StatistiquesAvancees);

[ApiController]
public sealed class BillingController : ControllerBase
{
    private static readonly string[] PeriodicitesValides = ["MENSUEL", "ANNUEL"];
    private static readonly string[] StatutsValides = ["ACTIF", "IMPAYE", "RESILIE"];

    private readonly SubscribeUseCase _subscribeUseCase;
    private readonly AdminListAbonnementsUseCase _listUseCase;
    private readonly RelanceUseCase _relanceUseCase;
    private readonly UpdateTarifUseCase _updateTarifUseCase;
    private readonly VerifyAbonnementPaiementUseCase _verifyPaiementUseCase;
    private readonly PlanAccessService _planAccessService;
    private readonly IAbonnementRepository _abonnements;

    public BillingController(
        SubscribeUseCase subscribeUseCase, AdminListAbonnementsUseCase listUseCase,
        RelanceUseCase relanceUseCase, UpdateTarifUseCase updateTarifUseCase,
        VerifyAbonnementPaiementUseCase verifyPaiementUseCase, PlanAccessService planAccessService, IAbonnementRepository abonnements)
    {
        _subscribeUseCase = subscribeUseCase;
        _listUseCase = listUseCase;
        _relanceUseCase = relanceUseCase;
        _updateTarifUseCase = updateTarifUseCase;
        _verifyPaiementUseCase = verifyPaiementUseCase;
        _planAccessService = planAccessService;
        _abonnements = abonnements;
    }

    [HttpGet("etablissements/{id:guid}/formule")]
    [ServiceFilter(typeof(PartnerOwnershipFilter))]
    public async Task<IActionResult> GetFormule(Guid id, CancellationToken cancellationToken) =>
        Ok(await _planAccessService.GetUsageAsync(id, cancellationToken));

    [HttpGet("etablissements/{id:guid}/abonnements")][ServiceFilter(typeof(PartnerOwnershipFilter))]
    public async Task<IActionResult> Historique(Guid id,CancellationToken ct)=>Ok(await _abonnements.ListerParEtablissementAsync(id,ct));

    [HttpGet("etablissements/{id:guid}/tarifs-abonnement")]
    [ServiceFilter(typeof(PartnerOwnershipFilter))]
    public async Task<IActionResult> GetTarifsPartenaire(Guid id, CancellationToken cancellationToken) =>
        Ok(await _updateTarifUseCase.ListerAsync(cancellationToken));

    [HttpGet("etablissements/{id:guid}/formules-disponibles")]
    [ServiceFilter(typeof(PartnerOwnershipFilter))]
    public async Task<IActionResult> GetFormulesPartenaire(Guid id, CancellationToken cancellationToken) =>
        Ok(await _planAccessService.ListAsync(cancellationToken));

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

    [HttpPost("admin/abonnements/{id:guid}/verifier-paiement")]
    [ServiceFilter(typeof(AdminApiKeyFilter))]
    [AdminRole("COMPTABLE")]
    public async Task<IActionResult> VerifierPaiement(Guid id, CancellationToken cancellationToken)
    {
        var result = await _verifyPaiementUseCase.ExecuteAsync(id, cancellationToken);

        if (!result.Success)
        {
            return result.Status == "not_found"
                ? NotFound()
                : StatusCode(StatusCodes.Status502BadGateway, new { status = result.Status });
        }

        return Ok(new { status = result.Status, statut = result.StatutAbonnement });
    }

    // Redirections client apres passage sur la page de paiement WiniPayer (cancel_url/return_url,
    // voir WinPayerGateway.InitiateAsync). Le resultat reel a deja ete applique par le callback
    // (source de verite) ; ces routes ne font qu'informer l'utilisateur, sans logique metier.
    [HttpGet("billing/winipayer/return")]
    public IActionResult WinPayerReturn() => Ok(new { message = "Paiement en cours de confirmation." });

    [HttpGet("billing/winipayer/cancel")]
    public IActionResult WinPayerCancel() => Ok(new { message = "Paiement annule." });

    [HttpGet("admin/abonnements")]
    [ServiceFilter(typeof(AdminApiKeyFilter))]
    [AdminRole("COMPTABLE")]
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
    [AdminRole("COMPTABLE")]
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
    [AdminRole("COMPTABLE")]
    public async Task<IActionResult> ListerTarifs(CancellationToken cancellationToken)
    {
        var tarifs = await _updateTarifUseCase.ListerAsync(cancellationToken);
        return Ok(tarifs);
    }

    [HttpPut("admin/tarifs/{periodicite}")]
    [ServiceFilter(typeof(AdminApiKeyFilter))]
    [AdminRole("SUPER_ADMIN")]
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

    [HttpGet("admin/formules")]
    [ServiceFilter(typeof(AdminApiKeyFilter))]
    [AdminRole("COMPTABLE")]
    public async Task<IActionResult> ListerFormules(CancellationToken cancellationToken) =>
        Ok(await _planAccessService.ListAsync(cancellationToken));

    [HttpPut("admin/formules/{formule}")]
    [ServiceFilter(typeof(AdminApiKeyFilter))]
    [AdminRole("SUPER_ADMIN")]
    public async Task<IActionResult> UpdateFormule(string formule, [FromBody] UpdateFormuleBody body, CancellationToken cancellationToken)
    {
        formule = formule.Trim().ToUpperInvariant();
        if (formule is not ("FREE" or "PRO") || body.LimitePrestations < 0 || body.LimiteRdvMensuels < 0)
        {
            return BadRequest(new { status = "configuration_invalide" });
        }

        var updated = await _planAccessService.UpdateAsync(new PlanEntitlements
        {
            Formule = formule,
            LimitePrestations = body.LimitePrestations,
            LimiteRdvMensuels = body.LimiteRdvMensuels,
            PaiementMobile = body.PaiementMobile,
            GestionEquipe = body.GestionEquipe,
            StatistiquesAvancees = body.StatistiquesAvancees,
        }, cancellationToken);
        return updated ? Ok(new { status = "updated" }) : NotFound();
    }
}
