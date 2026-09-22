using KekeBeauty.Api.Auth;
using KekeBeauty.Application.Billing;
using Microsoft.AspNetCore.Mvc;

namespace KekeBeauty.Api.Controllers;

public sealed record SubscribeBody(string Formule, string Periodicite);
public sealed record UpdateFormuleBody(
    string Libelle, bool EstActif, int OrdreAffichage, decimal? TarifMensuel, decimal? TarifAnnuel,
    int? LimitePrestations, int? LimiteRdvMensuels, bool PaiementMobile, bool GestionEquipe, bool StatistiquesAvancees,
    List<string>? Avantages, int? PromoPourcentage, DateTimeOffset? PromoFin);
public sealed record CreateFormuleBody(
    string Formule, string Libelle, int OrdreAffichage, decimal? TarifMensuel, decimal? TarifAnnuel,
    int? LimitePrestations, int? LimiteRdvMensuels, bool PaiementMobile, bool GestionEquipe, bool StatistiquesAvancees,
    List<string>? Avantages, int? PromoPourcentage, DateTimeOffset? PromoFin);

[ApiController]
public sealed class BillingController : ControllerBase
{
    private static readonly string[] PeriodicitesValides = ["MENSUEL", "ANNUEL"];
    private static readonly string[] StatutsValides = ["ACTIF", "IMPAYE", "RESILIE"];
    private static readonly System.Text.RegularExpressions.Regex CodeFormuleValide = new("^[A-Z0-9_]{2,30}$");

    private readonly SubscribeUseCase _subscribeUseCase;
    private readonly AdminListAbonnementsUseCase _listUseCase;
    private readonly RelanceUseCase _relanceUseCase;
    private readonly VerifyAbonnementPaiementUseCase _verifyPaiementUseCase;
    private readonly PlanAccessService _planAccessService;
    private readonly IAbonnementRepository _abonnements;

    public BillingController(
        SubscribeUseCase subscribeUseCase, AdminListAbonnementsUseCase listUseCase,
        RelanceUseCase relanceUseCase,
        VerifyAbonnementPaiementUseCase verifyPaiementUseCase, PlanAccessService planAccessService, IAbonnementRepository abonnements)
    {
        _subscribeUseCase = subscribeUseCase;
        _listUseCase = listUseCase;
        _relanceUseCase = relanceUseCase;
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

    [HttpGet("etablissements/{id:guid}/formules-disponibles")]
    [ServiceFilter(typeof(PartnerOwnershipFilter))]
    public async Task<IActionResult> GetFormulesPartenaire(Guid id, CancellationToken cancellationToken) =>
        Ok(await _planAccessService.ListAsync(cancellationToken));

    [HttpPost("etablissements/{id:guid}/abonnements")]
    [ServiceFilter(typeof(PartnerOwnershipFilter))]
    public async Task<IActionResult> Subscribe(Guid id, [FromBody] SubscribeBody body, CancellationToken cancellationToken)
    {
        if (!PeriodicitesValides.Contains(body.Periodicite) || string.IsNullOrWhiteSpace(body.Formule))
        {
            return BadRequest(new { status = "invalid_input" });
        }

        var result = await _subscribeUseCase.ExecuteAsync(id, body.Formule.Trim().ToUpperInvariant(), body.Periodicite, cancellationToken);

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

    [HttpGet("admin/formules")]
    [ServiceFilter(typeof(AdminApiKeyFilter))]
    [AdminRole("COMPTABLE")]
    public async Task<IActionResult> ListerFormules(CancellationToken cancellationToken) =>
        Ok(await _planAccessService.ListAsync(cancellationToken));

    [HttpPost("admin/formules")]
    [ServiceFilter(typeof(AdminApiKeyFilter))]
    [AdminRole("SUPER_ADMIN")]
    public async Task<IActionResult> CreerFormule([FromBody] CreateFormuleBody body, CancellationToken cancellationToken)
    {
        var formule = body.Formule.Trim().ToUpperInvariant();
        if (!CodeFormuleValide.IsMatch(formule) || string.IsNullOrWhiteSpace(body.Libelle)
            || body.LimitePrestations < 0 || body.LimiteRdvMensuels < 0
            || body.TarifMensuel < 0 || body.TarifAnnuel < 0
            || body.PromoPourcentage is < 1 or > 95)
        {
            return BadRequest(new { status = "configuration_invalide" });
        }

        var created = await _planAccessService.CreateAsync(new PlanEntitlements
        {
            Formule = formule,
            Libelle = body.Libelle.Trim(),
            EstActif = true,
            OrdreAffichage = body.OrdreAffichage,
            TarifMensuel = body.TarifMensuel,
            TarifAnnuel = body.TarifAnnuel,
            LimitePrestations = body.LimitePrestations,
            LimiteRdvMensuels = body.LimiteRdvMensuels,
            PaiementMobile = body.PaiementMobile,
            GestionEquipe = body.GestionEquipe,
            StatistiquesAvancees = body.StatistiquesAvancees,
            Avantages = (body.Avantages ?? []).Where(a => !string.IsNullOrWhiteSpace(a)).Select(a => a.Trim()).ToList(),
            PromoPourcentage = body.PromoPourcentage,
            PromoFin = body.PromoFin,
        }, cancellationToken);

        return created ? StatusCode(StatusCodes.Status201Created, new { formule }) : Conflict(new { status = "formule_existe_deja" });
    }

    [HttpPut("admin/formules/{formule}")]
    [ServiceFilter(typeof(AdminApiKeyFilter))]
    [AdminRole("SUPER_ADMIN")]
    public async Task<IActionResult> UpdateFormule(string formule, [FromBody] UpdateFormuleBody body, CancellationToken cancellationToken)
    {
        formule = formule.Trim().ToUpperInvariant();
        if (string.IsNullOrWhiteSpace(body.Libelle) || body.LimitePrestations < 0 || body.LimiteRdvMensuels < 0
            || body.TarifMensuel < 0 || body.TarifAnnuel < 0
            || body.PromoPourcentage is < 1 or > 95)
        {
            return BadRequest(new { status = "configuration_invalide" });
        }

        var updated = await _planAccessService.UpdateAsync(new PlanEntitlements
        {
            Formule = formule,
            Libelle = body.Libelle.Trim(),
            EstActif = body.EstActif,
            OrdreAffichage = body.OrdreAffichage,
            TarifMensuel = body.TarifMensuel,
            TarifAnnuel = body.TarifAnnuel,
            LimitePrestations = body.LimitePrestations,
            LimiteRdvMensuels = body.LimiteRdvMensuels,
            PaiementMobile = body.PaiementMobile,
            GestionEquipe = body.GestionEquipe,
            StatistiquesAvancees = body.StatistiquesAvancees,
            Avantages = (body.Avantages ?? []).Where(a => !string.IsNullOrWhiteSpace(a)).Select(a => a.Trim()).ToList(),
            PromoPourcentage = body.PromoPourcentage,
            PromoFin = body.PromoFin,
        }, cancellationToken);
        return updated ? Ok(new { status = "updated" }) : NotFound();
    }

    [HttpPut("admin/formules/{formule}/defaut")]
    [ServiceFilter(typeof(AdminApiKeyFilter))]
    [AdminRole("SUPER_ADMIN")]
    public async Task<IActionResult> DefinirFormuleParDefaut(string formule, CancellationToken cancellationToken)
    {
        var ok = await _planAccessService.SetDefaultAsync(formule.Trim().ToUpperInvariant(), cancellationToken);
        return ok ? Ok(new { status = "updated" }) : BadRequest(new { status = "formule_invalide_ou_inactive" });
    }

    [HttpDelete("admin/formules/{formule}")]
    [ServiceFilter(typeof(AdminApiKeyFilter))]
    [AdminRole("SUPER_ADMIN")]
    public async Task<IActionResult> SupprimerFormule(string formule, CancellationToken cancellationToken)
    {
        var result = await _planAccessService.DeleteAsync(formule.Trim().ToUpperInvariant(), cancellationToken);
        return result switch
        {
            DeleteFormuleResult.Deleted => Ok(new { status = "deleted" }),
            DeleteFormuleResult.NotFound => NotFound(),
            DeleteFormuleResult.EstFormuleParDefaut => Conflict(new { status = "formule_par_defaut" }),
            DeleteFormuleResult.DerniereFormule => Conflict(new { status = "derniere_formule" }),
            DeleteFormuleResult.FormuleUtilisee => Conflict(new { status = "formule_utilisee" }),
            _ => Conflict(new { status = "suppression_impossible" }),
        };
    }
}
