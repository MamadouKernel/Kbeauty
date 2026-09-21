using KekeBeauty.Api.Auth;
using KekeBeauty.Application.Partner;
using KekeBeauty.Application.Billing;
using KekeBeauty.Application.Directory;
using KekeBeauty.Application.Onboarding;
using System.Globalization;
using Microsoft.AspNetCore.Mvc;

namespace KekeBeauty.Api.Controllers;

public sealed record PrestationRequest(string LibellePrestation, decimal Tarif, short DureeMinutes);
public sealed record AssignCategoryPartnerRequest(string LibelleCategorie);
public sealed record CollaborateurRequest(string Nom, string? Specialite, string? Telephone);
public sealed record AssignerCollaborateurBody(Guid? IdCollaborateur);
public sealed record ModePaiementServiceRequest(string ModePaiementService, bool PaiementWave, bool PaiementOrangeMoney, bool PaiementMoovMoney);
public sealed record BoutiqueProfileRequest(string Nom, string? Description, string Telephone, decimal Latitude, decimal Longitude, string? Horaires);
public sealed record IndisponibiliteRequest(DateTimeOffset DateDebut, DateTimeOffset DateFin, string? Motif);

[ApiController]
[Route("partenaire/etablissements/{id:guid}")]
[ServiceFilter(typeof(PartnerOwnershipFilter))]
public sealed class PartnerManagementController : ControllerBase
{
    private readonly ManagePrestationsUseCase _prestationsUseCase;
    private readonly ManageCategoriesUseCase _categoriesUseCase;
    private readonly IPartnerStatsRepository _statsRepository;
    private readonly ManageEquipeUseCase _equipeUseCase;
    private readonly AssignerCollaborateurUseCase _assignerCollaborateurUseCase;
    private readonly PlanAccessService _planAccessService;
    private readonly IDirectoryRepository _directoryRepository;
    private readonly IFileStorage _fileStorage;
    private readonly IEtablissementRepository _etablissementRepository;

    public PartnerManagementController(
        ManagePrestationsUseCase prestationsUseCase, ManageCategoriesUseCase categoriesUseCase,
        IPartnerStatsRepository statsRepository, ManageEquipeUseCase equipeUseCase,
        AssignerCollaborateurUseCase assignerCollaborateurUseCase, PlanAccessService planAccessService,
        IDirectoryRepository directoryRepository, IFileStorage fileStorage, IEtablissementRepository etablissementRepository)
    {
        _prestationsUseCase = prestationsUseCase;
        _categoriesUseCase = categoriesUseCase;
        _statsRepository = statsRepository;
        _equipeUseCase = equipeUseCase;
        _assignerCollaborateurUseCase = assignerCollaborateurUseCase;
        _planAccessService = planAccessService;
        _directoryRepository = directoryRepository;
        _fileStorage = fileStorage;
        _etablissementRepository = etablissementRepository;
    }

    [HttpPost("medias")][RequestSizeLimit(10_000_000)]
    public async Task<IActionResult> AddMedia(Guid id, IFormFile photo, CancellationToken ct)
    { if(photo is null||photo.Length==0)return BadRequest(new{status="photo_requise"});var ext=Path.GetExtension(photo.FileName).TrimStart('.').ToLowerInvariant();if(ext is not("jpg" or "jpeg" or "png" or "webp"))return BadRequest(new{status="format_invalide"});var mediaId=Guid.NewGuid();await using var stream=photo.OpenReadStream();var path=await _fileStorage.SaveAsync(id,$"media-{mediaId}",ext,stream,ct);var saved=await _prestationsUseCase.AddMediaAsync(id,path,0,ct);return StatusCode(201,new{idMedia=saved}); }
    [HttpDelete("medias/{idMedia:guid}")]
    public async Task<IActionResult> DeleteMedia(Guid id,Guid idMedia,CancellationToken ct)=>await _prestationsUseCase.DeleteMediaAsync(id,idMedia,ct)?NoContent():NotFound();
    [HttpPost("kyc/resoumettre")][RequestSizeLimit(26_000_000)]
    public async Task<IActionResult> ResubmitKyc(Guid id, IFormFile? photoDevanture, IFormFile? documentRecto,
        IFormFile? documentVerso, [FromForm] string? typeDocumentIdentite, CancellationToken ct)
    {
        var type = typeDocumentIdentite?.Trim().ToUpperInvariant();
        if (type is not null and not ("CNI" or "PASSEPORT")) return BadRequest(new { status="type_document_invalide" });
        if (photoDevanture is null && documentRecto is null && documentVerso is null) return BadRequest(new { status="document_requis" });
        if (type == "CNI" && documentRecto is not null && documentVerso is null) return BadRequest(new { status="verso_requis" });
        if (type == "PASSEPORT" && documentVerso is not null) return BadRequest(new { status="verso_interdit" });
        static bool Allowed(IFormFile? file) => file is null || Path.GetExtension(file.FileName).TrimStart('.').ToLowerInvariant() is "jpg" or "jpeg" or "png" or "webp" or "pdf";
        if (!Allowed(photoDevanture) || !Allowed(documentRecto) || !Allowed(documentVerso)) return BadRequest(new { status="format_invalide" });
        string? photoPath=null, rectoPath=null, versoPath=null;
        if(photoDevanture is not null){await using var stream=photoDevanture.OpenReadStream();photoPath=await _fileStorage.SaveAsync(id,"devanture",Path.GetExtension(photoDevanture.FileName),stream,ct);}
        if(documentRecto is not null){await using var stream=documentRecto.OpenReadStream();rectoPath=await _fileStorage.SaveAsync(id,"document-recto",Path.GetExtension(documentRecto.FileName),stream,ct);}
        if(documentVerso is not null){await using var stream=documentVerso.OpenReadStream();versoPath=await _fileStorage.SaveAsync(id,"document-verso",Path.GetExtension(documentVerso.FileName),stream,ct);}
        return await _etablissementRepository.ResubmitAsync(id,photoPath,rectoPath,versoPath,type,ct)
            ? Ok(new{status="EN_ATTENTE"}) : Conflict(new{status="resoumission_impossible"});
    }
    [HttpGet]
    public async Task<IActionResult> GetManagedDetail(Guid id, CancellationToken cancellationToken)
    {
        var core = await _directoryRepository.GetManagedCoreAsync(id, cancellationToken);
        if (core is null) return NotFound();
        var medias = await _directoryRepository.GetMediasAsync(id, cancellationToken);
        var prestations = await _directoryRepository.GetPrestationsAsync(id, cancellationToken);
        var route = string.Create(CultureInfo.InvariantCulture,
            $"https://www.google.com/maps/dir/?api=1&destination={core.GpsLatitude},{core.GpsLongitude}");
        return Ok(new EtablissementDetail(core.IdEtablissement, core.NomEtablissement, core.Description,
            core.NumeroServiceClient, route, medias, prestations, core.GpsLatitude, core.GpsLongitude,
            core.ModePaiementService, core.PaiementWave, core.PaiementOrangeMoney, core.PaiementMoovMoney,
            false, core.Horaires, core.MotifRejet, core.StatutKyc));
    }

    [HttpPut("profil")]
    public async Task<IActionResult> UpdateProfile(Guid id, [FromBody] BoutiqueProfileRequest request, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.Nom) || string.IsNullOrWhiteSpace(request.Telephone)
            || request.Latitude is < -90 or > 90 || request.Longitude is < -180 or > 180)
            return BadRequest(new { status = "profil_invalide" });
        var updated = await _prestationsUseCase.UpdateProfilBoutiqueAsync(id, request.Nom.Trim(),
            request.Description?.Trim(), request.Telephone.Trim(), request.Latitude, request.Longitude,
            request.Horaires, cancellationToken);
        return updated ? Ok(new { status = "updated" }) : NotFound();
    }

    [HttpGet("indisponibilites")]
    public async Task<IActionResult> ListIndisponibilites(Guid id, CancellationToken ct) =>
        Ok(await _prestationsUseCase.ListIndisponibilitesAsync(id, ct));

    [HttpPost("indisponibilites")]
    public async Task<IActionResult> AddIndisponibilite(Guid id, [FromBody] IndisponibiliteRequest request, CancellationToken ct)
    {
        if (request.DateFin <= request.DateDebut) return BadRequest(new { status = "periode_invalide" });
        var created = await _prestationsUseCase.AddIndisponibiliteAsync(id, request.DateDebut, request.DateFin, request.Motif?.Trim(), ct);
        return StatusCode(201, new { idIndisponibilite = created });
    }

    [HttpDelete("indisponibilites/{idIndisponibilite:guid}")]
    public async Task<IActionResult> DeleteIndisponibilite(Guid id, Guid idIndisponibilite, CancellationToken ct) =>
        await _prestationsUseCase.DeleteIndisponibiliteAsync(id, idIndisponibilite, ct) ? NoContent() : NotFound();

    [HttpGet("collaborateurs")]
    public async Task<IActionResult> ListerEquipe(Guid id, CancellationToken cancellationToken)
    {
        var equipe = await _equipeUseCase.ListerAsync(id, cancellationToken);
        return Ok(equipe);
    }

    [HttpPost("collaborateurs")]
    public async Task<IActionResult> AjouterCollaborateur(Guid id, [FromBody] CollaborateurRequest request, CancellationToken cancellationToken)
    {
        if (!(await _planAccessService.GetEntitlementsAsync(id, cancellationToken)).GestionEquipe)
        {
            return StatusCode(StatusCodes.Status403Forbidden, new { status = "gestion_equipe_non_incluse" });
        }
        if (string.IsNullOrWhiteSpace(request.Nom))
        {
            return BadRequest(new { status = "nom_requis" });
        }

        var idCollaborateur = await _equipeUseCase.AjouterAsync(id, request.Nom, request.Specialite, request.Telephone, cancellationToken);
        return StatusCode(StatusCodes.Status201Created, new { idCollaborateur });
    }

    [HttpDelete("collaborateurs/{idCollaborateur:guid}")]
    public async Task<IActionResult> RetirerCollaborateur(Guid id, Guid idCollaborateur, CancellationToken cancellationToken)
    {
        var removed = await _equipeUseCase.RetirerAsync(id, idCollaborateur, cancellationToken);
        return removed ? Ok(new { status = "removed" }) : NotFound();
    }

    [HttpPost("rdv/{idRdv:guid}/assigner")]
    public async Task<IActionResult> AssignerCollaborateur(Guid id, Guid idRdv, [FromBody] AssignerCollaborateurBody body, CancellationToken cancellationToken)
    {
        if (!(await _planAccessService.GetEntitlementsAsync(id, cancellationToken)).GestionEquipe)
        {
            return StatusCode(StatusCodes.Status403Forbidden, new { status = "gestion_equipe_non_incluse" });
        }
        var result = await _assignerCollaborateurUseCase.ExecuteAsync(id, idRdv, body.IdCollaborateur, cancellationToken);
        return result switch
        {
            "assigned" => Ok(new { status = result }),
            "rdv_introuvable" => NotFound(),
            "collaboratrice_invalide" => BadRequest(new { status = result }),
            _ => BadRequest(new { status = result }),
        };
    }

    [HttpGet("statistiques")]
    public async Task<IActionResult> GetStatistiques(Guid id, CancellationToken cancellationToken)
    {
        if (!(await _planAccessService.GetEntitlementsAsync(id, cancellationToken)).StatistiquesAvancees)
        {
            return StatusCode(StatusCodes.Status403Forbidden, new { status = "statistiques_non_incluses" });
        }
        var stats = await _statsRepository.GetStatistiquesAsync(id, cancellationToken);
        return Ok(stats);
    }

    [HttpPost("prestations")]
    public async Task<IActionResult> AddPrestation(Guid id, [FromBody] PrestationRequest request, CancellationToken cancellationToken)
    {
        if (!await _planAccessService.CanAddPrestationAsync(id, cancellationToken))
        {
            var droits = await _planAccessService.GetEntitlementsAsync(id, cancellationToken);
            return Conflict(new { status = "limite_prestations_atteinte", limite = droits.LimitePrestations });
        }
        var idPrestation = await _prestationsUseCase.AddAsync(id, request.LibellePrestation, request.Tarif, request.DureeMinutes, cancellationToken);
        return StatusCode(StatusCodes.Status201Created, new { idPrestation });
    }

    [HttpPut("prestations/{idPrestation:guid}")]
    public async Task<IActionResult> UpdatePrestation(Guid id, Guid idPrestation, [FromBody] PrestationRequest request, CancellationToken cancellationToken)
    {
        var updated = await _prestationsUseCase.UpdateAsync(id, idPrestation, request.LibellePrestation, request.Tarif, request.DureeMinutes, cancellationToken);
        return updated ? Ok(new { status = "updated" }) : NotFound();
    }

    [HttpDelete("prestations/{idPrestation:guid}")]
    public async Task<IActionResult> DeletePrestation(Guid id, Guid idPrestation, CancellationToken cancellationToken)
    {
        var deleted = await _prestationsUseCase.DeleteAsync(id, idPrestation, cancellationToken);
        return deleted ? Ok(new { status = "deleted" }) : NotFound();
    }

    [HttpPut("paiement-services")]
    public async Task<IActionResult> UpdateModePaiementService(Guid id, [FromBody] ModePaiementServiceRequest request, CancellationToken cancellationToken)
    {
        var mode = request.ModePaiementService?.Trim().ToUpperInvariant();
        if (mode is not ("ESPECES" or "EN_LIGNE" or "MIXTE") ||
            (mode != "ESPECES" && !request.PaiementWave && !request.PaiementOrangeMoney && !request.PaiementMoovMoney))
        {
            return BadRequest(new { status = "mode_paiement_invalide" });
        }

        if (mode != "ESPECES" && !(await _planAccessService.GetEntitlementsAsync(id, cancellationToken)).PaiementMobile)
        {
            return StatusCode(StatusCodes.Status403Forbidden, new { status = "paiement_mobile_non_inclus" });
        }

        var boutique = await _directoryRepository.GetManagedCoreAsync(id, cancellationToken);
        if (mode != "ESPECES" && boutique?.StatutKyc != "VALIDE")
        {
            return StatusCode(StatusCodes.Status403Forbidden, new { status = "validation_kyc_requise" });
        }

        var updated = await _prestationsUseCase.UpdateModePaiementServiceAsync(id, mode,
            request.PaiementWave, request.PaiementOrangeMoney, request.PaiementMoovMoney, cancellationToken);
        return updated ? Ok(new { status = "updated", modePaiementService = mode }) : NotFound();
    }

    [HttpPost("categories")]
    public async Task<IActionResult> AssignCategory(Guid id, [FromBody] AssignCategoryPartnerRequest request, CancellationToken cancellationToken)
    {
        var idCategorie = await _categoriesUseCase.AssignAsync(id, request.LibelleCategorie, cancellationToken);
        return Ok(new { idCategorie });
    }

    [HttpDelete("categories/{idCategorie:guid}")]
    public async Task<IActionResult> RemoveCategory(Guid id, Guid idCategorie, CancellationToken cancellationToken)
    {
        await _categoriesUseCase.RemoveAsync(id, idCategorie, cancellationToken);
        return Ok(new { status = "removed" });
    }
}
