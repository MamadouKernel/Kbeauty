using KekeBeauty.Api.Auth;
using KekeBeauty.Application.Admin;
using Microsoft.AspNetCore.Mvc;
namespace KekeBeauty.Api.Controllers;
[ApiController][Route("admin/comptes")][ServiceFilter(typeof(AdminApiKeyFilter))][AdminRole("SUPER_ADMIN")]
public sealed class AdminAccountsController(IAdminManagementRepository repo):ControllerBase {
 public sealed record CreateBody(string Nom,string Email,string MotDePasse,string Role);
 [HttpGet] public async Task<IActionResult> List(CancellationToken ct)=>Ok(await repo.ListAdminsAsync(ct));
 [HttpPost] public async Task<IActionResult> Create(CreateBody b,CancellationToken ct){var roles=new[]{"SUPER_ADMIN","KYC","SUPPORT","COMPTABLE"};if(string.IsNullOrWhiteSpace(b.Nom)||string.IsNullOrWhiteSpace(b.Email)||!AdminPasswordPolicy.IsValid(b.MotDePasse)||!roles.Contains(b.Role))return BadRequest(new{status="donnees_invalides"});if(await repo.FindByEmailAsync(b.Email,ct) is not null)return Conflict(new{status="email_existant"});var id=await repo.CreateAdminAsync(b.Nom.Trim(),b.Email.Trim(),AdminAuthController.Hash(b.MotDePasse),b.Role,ct);return Created($"admin/comptes/{id}",new{idAdmin=id});}
 [HttpPut("{id:guid}/actif")] public async Task<IActionResult> Active(Guid id,[FromBody]Dictionary<string,bool> body,CancellationToken ct){var me=(AdminIdentity)HttpContext.Items["Admin"]!;var active=body.GetValueOrDefault("actif");if(id==me.Id&&!active)return Conflict(new{status="auto_desactivation_interdite"});return await repo.SetAdminActiveAsync(id,active,ct)?Ok(new{id,actif=active}):NotFound();}
}
