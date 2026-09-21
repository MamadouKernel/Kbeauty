using KekeBeauty.Api.Auth;
using KekeBeauty.Application.Rdv;
using KekeBeauty.Application.Notifications;
using KekeBeauty.Application.Billing;
using Microsoft.AspNetCore.Mvc;

namespace KekeBeauty.Api.Controllers;

public sealed record WalkInBody(Guid IdPrestation, Guid? IdCollaborateur, string NomClient, string TelephoneClient, DateTimeOffset DateHeureDebut, string ModePaiement);


public sealed record RequestRdvBody(Guid IdEtablissement, Guid IdPrestation, DateTimeOffset DateHeureDebut, string? ModePaiementChoisi);
public sealed record ReprogrammerBody(DateTimeOffset DateHeureDebut);
public sealed record LaisserAvisBody(short Note, string? Commentaire);
public sealed record DeclarerLitigeBody(string Motif);
public sealed record LaisserPourboireBody(Guid IdCollaborateur, decimal Montant);
public sealed record SignalerRetardBody(int Minutes);

[ApiController]
public sealed class RdvController : ControllerBase
{
    private readonly RequestRdvUseCase _requestUseCase;
    private readonly DecideRdvUseCase _decideUseCase;
    private readonly IRdvRepository _rdvRepository;
    private readonly InitierPaiementRdvUseCase _initierPaiementUseCase;
    private readonly VerifyRdvPaiementUseCase _verifyPaiementUseCase;
    private readonly LaisserAvisUseCase _laisserAvisUseCase;
    private readonly IAvisRepository _avisRepository;
    private readonly RelancerPaiementRdvUseCase _relancerPaiementUseCase;
    private readonly AnnulerRdvUseCase _annulerRdvUseCase;
    private readonly DeclarerLitigeUseCase _declarerLitigeUseCase;
    private readonly LaisserPourboireUseCase _laisserPourboireUseCase;
    private readonly INotificationRepository _notificationRepository;
    private readonly PlanAccessService _planAccessService;
    private readonly PartnerSessionTokenService _partnerTokens;
    private readonly RdvQrTokenService _qrTokens;
    private readonly IRdvPaiementRepository _paiementRepository;

    public RdvController(
        RequestRdvUseCase requestUseCase, DecideRdvUseCase decideUseCase, IRdvRepository rdvRepository,
        InitierPaiementRdvUseCase initierPaiementUseCase, VerifyRdvPaiementUseCase verifyPaiementUseCase,
        LaisserAvisUseCase laisserAvisUseCase, IAvisRepository avisRepository,
        RelancerPaiementRdvUseCase relancerPaiementUseCase, AnnulerRdvUseCase annulerRdvUseCase,
        DeclarerLitigeUseCase declarerLitigeUseCase, LaisserPourboireUseCase laisserPourboireUseCase,
        INotificationRepository notificationRepository, PlanAccessService planAccessService, PartnerSessionTokenService partnerTokens, RdvQrTokenService qrTokens,
        IRdvPaiementRepository paiementRepository)
    {
        _requestUseCase = requestUseCase;
        _decideUseCase = decideUseCase;
        _rdvRepository = rdvRepository;
        _initierPaiementUseCase = initierPaiementUseCase;
        _verifyPaiementUseCase = verifyPaiementUseCase;
        _laisserAvisUseCase = laisserAvisUseCase;
        _avisRepository = avisRepository;
        _relancerPaiementUseCase = relancerPaiementUseCase;
        _annulerRdvUseCase = annulerRdvUseCase;
        _declarerLitigeUseCase = declarerLitigeUseCase;
        _laisserPourboireUseCase = laisserPourboireUseCase;
        _notificationRepository = notificationRepository;
        _planAccessService = planAccessService;
        _partnerTokens = partnerTokens;
        _qrTokens = qrTokens;
        _paiementRepository = paiementRepository;
    }

    [HttpGet("etablissements/{id:guid}/creneaux")]
    public async Task<IActionResult> GetCreneaux(Guid id, [FromQuery] DateOnly date, CancellationToken cancellationToken)
    {
        var creneaux = await _requestUseCase.GetCreneauxOccupesAsync(id, date, cancellationToken);
        return Ok(creneaux);
    }

    [HttpPost("rdv")]
    public async Task<IActionResult> RequestRdv([FromBody] RequestRdvBody body, CancellationToken cancellationToken)
    {
        if (!Request.Headers.TryGetValue("X-Client-Id", out var clientIdHeader) || !Guid.TryParse(clientIdHeader, out var idClient))
        {
            return Unauthorized(new { status = "unauthorized" });
        }

        if (!await _planAccessService.CanCreateBookingAsync(body.IdEtablissement, cancellationToken))
        {
            var droits = await _planAccessService.GetEntitlementsAsync(body.IdEtablissement, cancellationToken);
            return Conflict(new { status = "limite_rdv_atteinte", limite = droits.LimiteRdvMensuels });
        }

        var result = await _requestUseCase.ExecuteAsync(body.IdEtablissement, body.IdPrestation, idClient, body.DateHeureDebut, body.ModePaiementChoisi, cancellationToken);

        if (!result.Success)
        {
            return result.Status == "slot_unavailable"
                ? Conflict(new { status = result.Status })
                : BadRequest(new { status = result.Status });
        }

        var modePaiementService = await _rdvRepository.GetModePaiementServiceRdvAsync(result.IdRdv!.Value, cancellationToken) ?? "ESPECES";
        if (modePaiementService != "EN_LIGNE")
        {
            return StatusCode(StatusCodes.Status201Created, new { idRdv = result.IdRdv, statut = result.Status, modePaiementService });
        }

        // Paiement en ligne optionnel (FR-004/FR-005) : un echec ici ne remet jamais en cause le
        // RDV deja cree ci-dessus, le client garde toujours la possibilite de payer sur place.
        var montant = await _rdvRepository.GetTarifPrestationAsync(body.IdPrestation, cancellationToken) ?? 0m;
        var paiement = await _initierPaiementUseCase.ExecuteAsync(result.IdRdv!.Value, montant, cancellationToken);

        return StatusCode(StatusCodes.Status201Created, new
        {
            idRdv = result.IdRdv,
            statut = result.Status,
            modePaiementService,
            paiement = new
            {
                statutPaiement = paiement.StatutPaiement,
                lienPaiement = paiement.LienPaiement,
                referenceExterne = paiement.ReferenceExterne,
            },
        });
    }

    [HttpGet("rdv")]
    public async Task<IActionResult> GetHistorique(CancellationToken cancellationToken)
    {
        if (!Request.Headers.TryGetValue("X-Client-Id", out var clientIdHeader) || !Guid.TryParse(clientIdHeader, out var idClient))
        {
            return Unauthorized(new { status = "unauthorized" });
        }

        var historique = await _rdvRepository.ListByClientAsync(idClient, cancellationToken);
        return Ok(historique);
    }

    [HttpPost("rdv/{id:guid}/paiement/verifier")]
    public async Task<IActionResult> VerifierPaiement(Guid id, CancellationToken cancellationToken)
    {
        if (!Request.Headers.TryGetValue("X-Client-Id", out var clientIdHeader) || !Guid.TryParse(clientIdHeader, out var idClient))
        {
            return Unauthorized(new { status = "unauthorized" });
        }

        // Verifie que le RDV appartient bien a ce client avant de reveler/agir sur son paiement.
        var rdv = await _rdvRepository.GetStatutAsync(id, idClient, cancellationToken);
        if (rdv is null)
        {
            return NotFound();
        }

        var result = await _verifyPaiementUseCase.ExecuteAsync(id, cancellationToken);
        if (!result.Success)
        {
            return result.Status == "not_configured"
                ? StatusCode(StatusCodes.Status503ServiceUnavailable, new { status = result.Status })
                : NotFound();
        }

        return Ok(new { statutPaiement = result.StatutPaiement });
    }

    [HttpGet("rdv/{id:guid}")]
    public async Task<IActionResult> GetStatut(Guid id, CancellationToken cancellationToken)
    {
        if (!Request.Headers.TryGetValue("X-Client-Id", out var clientIdHeader) || !Guid.TryParse(clientIdHeader, out var idClient))
        {
            return Unauthorized(new { status = "unauthorized" });
        }

        var rdv = (await _rdvRepository.ListByClientAsync(idClient, cancellationToken)).FirstOrDefault(x => x.IdRdv == id);
        return rdv is null ? NotFound() : Ok(rdv);
    }

    [HttpPost("rdv/{id:guid}/paiement/relancer")]
    public async Task<IActionResult> RelancerPaiement(Guid id, CancellationToken cancellationToken)
    {
        if (!Request.Headers.TryGetValue("X-Client-Id", out var clientIdHeader) || !Guid.TryParse(clientIdHeader, out var idClient))
        {
            return Unauthorized(new { status = "unauthorized" });
        }

        var rdv = await _rdvRepository.GetStatutAsync(id, idClient, cancellationToken);
        if (rdv is null)
        {
            return NotFound();
        }

        var result = await _relancerPaiementUseCase.ExecuteAsync(id, cancellationToken);
        if (!result.Success)
        {
            return result.Status == "payment_initiation_failed"
                ? StatusCode(StatusCodes.Status502BadGateway, new { status = result.Status })
                : Conflict(new { status = result.Status });
        }

        return Ok(new { statutPaiement = result.Status, lienPaiement = result.LienPaiement });
    }

    [HttpPost("rdv/{id:guid}/annuler")]
    public async Task<IActionResult> Annuler(Guid id, CancellationToken cancellationToken)
    {
        if (!Request.Headers.TryGetValue("X-Client-Id", out var clientIdHeader) || !Guid.TryParse(clientIdHeader, out var idClient))
        {
            return Unauthorized(new { status = "unauthorized" });
        }

        var result = await _annulerRdvUseCase.ExecuteAsync(id, idClient, cancellationToken);
        return result.Success
            ? Ok(new { statut = result.Status, remboursementEnCours = result.RemboursementEnCours })
            : NotFound();
    }

    [HttpPost("rdv/{id:guid}/avis")]
    public async Task<IActionResult> LaisserAvis(Guid id, [FromBody] LaisserAvisBody body, CancellationToken cancellationToken)
    {
        if (!Request.Headers.TryGetValue("X-Client-Id", out var clientIdHeader) || !Guid.TryParse(clientIdHeader, out var idClient))
        {
            return Unauthorized(new { status = "unauthorized" });
        }

        var result = await _laisserAvisUseCase.ExecuteAsync(id, idClient, body.Note, body.Commentaire, cancellationToken);

        if (!result.Success)
        {
            return result.Status switch
            {
                "note_invalide" => BadRequest(new { status = result.Status }),
                "avis_deja_existant" => Conflict(new { status = result.Status }),
                _ => NotFound(),
            };
        }

        return StatusCode(StatusCodes.Status201Created, new { idAvis = result.IdAvis });
    }

    [HttpGet("etablissements/{id:guid}/avis")]
    public async Task<IActionResult> GetAvis(Guid id, CancellationToken cancellationToken)
    {
        var avis = await _avisRepository.ListerParEtablissementAsync(id, cancellationToken);
        return Ok(avis);
    }

    [HttpGet("partenaire/etablissements/{id:guid}/rdv")]
    [ServiceFilter(typeof(PartnerOwnershipFilter))]
    public async Task<IActionResult> ListerRdvEtablissement(Guid id, CancellationToken cancellationToken)
    {
        var rdvs = await _rdvRepository.ListByEtablissementAsync(id, cancellationToken);
        return Ok(rdvs);
    }

    [HttpPost("partenaire/etablissements/{id:guid}/rdv/comptoir")]
    [ServiceFilter(typeof(PartnerOwnershipFilter))]
    public async Task<IActionResult> CreerRdvComptoir(Guid id, [FromBody] WalkInBody body, CancellationToken cancellationToken)
    {
        var phone = new string((body.TelephoneClient ?? string.Empty).Where(c => char.IsDigit(c) || c == '+').ToArray());
        if(string.IsNullOrWhiteSpace(body.NomClient) || phone.Length < 10 || body.DateHeureDebut < DateTimeOffset.Now.AddMinutes(-5))
            return BadRequest(new{status="informations_invalides"});
        var result=await _rdvRepository.CreateWalkInAsync(id,body.IdPrestation,body.IdCollaborateur,body.NomClient.Trim(),phone,body.DateHeureDebut,body.ModePaiement,cancellationToken);
        return result.IdRdv is not null ? StatusCode(StatusCodes.Status201Created,new{idRdv=result.IdRdv,status=result.Status}) : result.Status switch
        {
            "creneau_indisponible" => Conflict(new{status=result.Status}),
            "collaboratrice_invalide" => BadRequest(new{status=result.Status}),
            _ => NotFound(new{status=result.Status})
        };
    }
    [HttpPost("partenaire/etablissements/{id:guid}/rdv/{idRdv:guid}/confirmer")]
    [ServiceFilter(typeof(PartnerOwnershipFilter))]
    public async Task<IActionResult> Confirmer(Guid id, Guid idRdv, CancellationToken cancellationToken)
    {
        var result = await _decideUseCase.ConfirmerAsync(id, idRdv, cancellationToken);
        return result.Success ? Ok(new { statut = result.Status }) : NotFound();
    }

    [HttpPost("partenaire/etablissements/{id:guid}/rdv/{idRdv:guid}/refuser")]
    [ServiceFilter(typeof(PartnerOwnershipFilter))]
    public async Task<IActionResult> Refuser(Guid id, Guid idRdv, CancellationToken cancellationToken)
    {
        var result = await _decideUseCase.RefuserAsync(id, idRdv, cancellationToken);
        return result.Success ? Ok(new { statut = result.Status }) : NotFound();
    }

    [HttpPost("partenaire/etablissements/{id:guid}/rdv/{idRdv:guid}/reprogrammer")]
    [ServiceFilter(typeof(PartnerOwnershipFilter))]
    public async Task<IActionResult> Reprogrammer(Guid id, Guid idRdv, [FromBody] ReprogrammerBody body, CancellationToken cancellationToken)
    {
        var result = await _decideUseCase.ReprogrammerAsync(id, idRdv, body.DateHeureDebut, cancellationToken);
        return result.Success ? Ok(new { statut = result.Status }) : Conflict(new { status = result.Status });
    }

    [HttpPost("rdv/{id:guid}/reprogrammer")]
    public async Task<IActionResult> ReprogrammerParClient(Guid id, [FromBody] ReprogrammerBody body, CancellationToken cancellationToken)
    {
        if (!Request.Headers.TryGetValue("X-Client-Id", out var clientIdHeader) || !Guid.TryParse(clientIdHeader, out var idClient))
        {
            return Unauthorized(new { status = "unauthorized" });
        }

        var rdv = await _rdvRepository.GetStatutAsync(id, idClient, cancellationToken);
        if (rdv is null || rdv.StatutRdv is not ("DEMANDE" or "CONFIRME"))
        {
            return NotFound(new { status = "not_found" });
        }
        if (body.DateHeureDebut <= DateTimeOffset.UtcNow)
        {
            return BadRequest(new { status = "date_invalide" });
        }

        var historique = await _rdvRepository.ListByClientAsync(idClient, cancellationToken);
        var item = historique.FirstOrDefault(x => x.IdRdv == id);
        if (item is null)
        {
            return NotFound(new { status = "not_found" });
        }

        var updated = await _rdvRepository.RescheduleAsync(item.IdEtablissement, id, body.DateHeureDebut, cancellationToken);
        if (!updated)
        {
            return Conflict(new { status = "slot_unavailable" });
        }

        var ownerId = await _rdvRepository.GetOwnerIdAsync(item.IdEtablissement, cancellationToken);
        if (ownerId is not null)
        {
            await _notificationRepository.CreerAsync(ownerId.Value, "Rendez-vous reprogrammé",
                $"La cliente a déplacé son rendez-vous au {body.DateHeureDebut.ToLocalTime():dd/MM/yyyy à HH:mm}.", id, cancellationToken);
        }

        return Ok(new { statut = "reprogramme", dateHeureDebut = body.DateHeureDebut });
    }

    [HttpPost("rdv/{id:guid}/retard")]
    public async Task<IActionResult> SignalerRetard(Guid id, [FromBody] SignalerRetardBody body, CancellationToken cancellationToken)
    {
        if (!Request.Headers.TryGetValue("X-Client-Id", out var clientIdHeader) || !Guid.TryParse(clientIdHeader, out var idClient))
        {
            return Unauthorized(new { status = "unauthorized" });
        }
        if (body.Minutes is not (10 or 15 or 30))
        {
            return BadRequest(new { status = "retard_invalide" });
        }

        var rdv = await _rdvRepository.GetStatutAsync(id, idClient, cancellationToken);
        if (rdv is null || rdv.StatutRdv != "CONFIRME")
        {
            return NotFound(new { status = "not_found" });
        }

        if (!await _rdvRepository.SignalerRetardAsync(id, idClient, (short)body.Minutes, cancellationToken))
        {
            return Conflict(new { status = "retard_non_modifiable" });
        }

        var historique = await _rdvRepository.ListByClientAsync(idClient, cancellationToken);
        var item = historique.FirstOrDefault(x => x.IdRdv == id);
        var ownerId = item is null ? null : await _rdvRepository.GetOwnerIdAsync(item.IdEtablissement, cancellationToken);
        if (ownerId is null)
        {
            return NotFound(new { status = "salon_introuvable" });
        }

        await _notificationRepository.CreerAsync(ownerId.Value, "Retard signalé",
            $"La cliente signale un retard estimé à {body.Minutes} minutes pour le rendez-vous {item!.LibellePrestation}.", id, cancellationToken);
        return Ok(new { statut = "retard_signale", minutes = body.Minutes });
    }
    // Parcours 6 : client ou gerant (identifie par le meme en-tete que ses autres actions) peut
    // declarer un litige sur un RDV.
    [HttpPost("rdv/{id:guid}/litige")]
    public async Task<IActionResult> DeclarerLitige(Guid id, [FromBody] DeclarerLitigeBody body, CancellationToken cancellationToken)
    {
        Guid? idUtilisateur = null;
        if (Request.Headers.TryGetValue("X-Client-Id", out var clientIdHeader) && Guid.TryParse(clientIdHeader, out var idClient))
        {
            idUtilisateur = idClient;
        }
        else if (_partnerTokens.TryFromRequest(Request, out var idPartner))
        {
            idUtilisateur = idPartner;
        }

        if (idUtilisateur is null)
        {
            return Unauthorized(new { status = "unauthorized" });
        }

        var result = await _declarerLitigeUseCase.ExecuteAsync(id, idUtilisateur.Value, body.Motif, cancellationToken);
        if (!result.Success)
        {
            return result.Status == "motif_requis" ? BadRequest(new { status = result.Status }) : NotFound();
        }

        return StatusCode(StatusCodes.Status201Created, new { idLitige = result.IdLitige });
    }

    // Parcours 3 : pourboire direct a la collaboratrice assignee (ou choisie) - 0% commission
    // plateforme. Voir LaisserPourboireUseCase pour le constat honnete sur le transfert des fonds.
    [HttpPost("rdv/{id:guid}/pourboire")]
    public async Task<IActionResult> LaisserPourboire(Guid id, [FromBody] LaisserPourboireBody body, CancellationToken cancellationToken)
    {
        if (!Request.Headers.TryGetValue("X-Client-Id", out var clientIdHeader) || !Guid.TryParse(clientIdHeader, out var idClient))
        {
            return Unauthorized(new { status = "unauthorized" });
        }

        var rdv = await _rdvRepository.GetStatutAsync(id, idClient, cancellationToken);
        if (rdv is null)
        {
            return NotFound();
        }

        var result = await _laisserPourboireUseCase.ExecuteAsync(id, body.IdCollaborateur, body.Montant, cancellationToken);
        if (!result.Success)
        {
            return result.Status switch
            {
                "montant_invalide" or "collaboratrice_invalide" => BadRequest(new { status = result.Status }),
                _ => StatusCode(StatusCodes.Status502BadGateway, new { status = result.Status }),
            };
        }

        return StatusCode(StatusCodes.Status201Created, new { idPourboire = result.IdPourboire, lienPaiement = result.LienPaiement });
    }

    [HttpGet("rdv/{id:guid}/qr")]
    public async Task<IActionResult> GetQr(Guid id,CancellationToken cancellationToken)
    {
        if(!Request.Headers.TryGetValue("X-Client-Id",out var header)||!Guid.TryParse(header,out var client))return Unauthorized();
        if(await _rdvRepository.GetStatutAsync(id,client,cancellationToken) is null)return NotFound();
        var token=_qrTokens.Create(id,client);var url=$"{Request.Scheme}://{Request.Host}/rdv/qr/verify?token={Uri.EscapeDataString(token)}";
        using var generator=new QRCoder.QRCodeGenerator();using var data=generator.CreateQrCode(url,QRCoder.QRCodeGenerator.ECCLevel.Q);var svg=new QRCoder.SvgQRCode(data).GetGraphic(8,"#31003f","#ffffff",true,QRCoder.SvgQRCode.SizingMode.ViewBoxAttribute);
        return Content(svg,"image/svg+xml",System.Text.Encoding.UTF8);
    }

    [HttpGet("rdv/qr/verify")]
    public async Task<IActionResult> VerifyQr([FromQuery]string token,CancellationToken cancellationToken)
    {
        if(!_partnerTokens.TryFromRequest(Request,out var partner))return Unauthorized(new{status="connexion_partenaire_requise"});
        if(!_qrTokens.TryValidate(token,out var rdv,out var client))return BadRequest(new{status="qr_invalide"});
        var item=await _rdvRepository.GetStatutAsync(rdv,client,cancellationToken);if(item is null)return NotFound(new{status="rdv_introuvable"});
        var history=await _rdvRepository.ListByClientAsync(client,cancellationToken);var detail=history.FirstOrDefault(x=>x.IdRdv==rdv);if(detail is null||await _rdvRepository.GetOwnerIdAsync(detail.IdEtablissement,cancellationToken)!=partner)return Forbid();
        var nomClient=await _rdvRepository.GetClientNomAsync(rdv,cancellationToken)??"";
        var paiement=await _paiementRepository.ObtenirParRdvAsync(rdv,cancellationToken);
        var acomptePaye=paiement is { StatutTransaction: "REUSSIE" } ? paiement.Montant : 0m;
        return Ok(new{idRdv=item.IdRdv,statut=item.StatutRdv,dateHeureDebut=item.DateHeureDebut,libellePrestation=detail.LibellePrestation,nomClient,acomptePaye,reference=$"KB-{item.IdRdv.ToString()[..8].ToUpperInvariant()}",verifie=true});
    }}
