using System.Text;
using KekeBeauty.Api.Auth;
using KekeBeauty.Application.Admin;
using Microsoft.AspNetCore.Mvc;
namespace KekeBeauty.Api.Controllers;
[ApiController][Route("admin/gestion")][ServiceFilter(typeof(AdminApiKeyFilter))]
public sealed class AdminManagementController(IAdminManagementRepository repo):ControllerBase {
 [HttpGet("utilisateurs")][AdminRole("SUPPORT")] public async Task<IActionResult> Users([FromQuery]string? q,CancellationToken ct)=>Ok(await repo.SearchUsersAsync(q,ct));
 [HttpGet("etablissements")][AdminRole("SUPPORT","KYC")] public async Task<IActionResult> Shops([FromQuery]string? q,CancellationToken ct)=>Ok(await repo.SearchShopsAsync(q,ct));
 [HttpGet("journal")][AdminRole("SUPER_ADMIN")] public async Task<IActionResult> Audit(CancellationToken ct)=>Ok(await repo.ListAuditAsync(ct));
 [HttpGet("export/{type}")][AdminRole("SUPPORT")] public async Task<IActionResult> Export(string type,[FromQuery]string? q,CancellationToken ct){string csv;if(type=="utilisateurs"){var rows=await repo.SearchUsersAsync(q,ct);csv="Nom;Telephone;Email;Type;Suspendu;Creation\n"+string.Join('\n',rows.Select(x=>$"{C(x.Nom)};{C(x.Telephone)};{C(x.Email)};{x.TypeCompte};{x.EstSuspendu};{x.DateCreation:O}"));}else if(type=="etablissements"){var rows=await repo.SearchShopsAsync(q,ct);csv="Boutique;Gerant;Telephone;KYC;Suspendue;Creation\n"+string.Join('\n',rows.Select(x=>$"{C(x.NomEtablissement)};{C(x.Gerant)};{C(x.Telephone)};{x.StatutKyc};{x.EstSuspendu};{x.DateCreation:O}"));}else return NotFound();return File(Encoding.UTF8.GetPreamble().Concat(Encoding.UTF8.GetBytes(csv)).ToArray(),"text/csv",$"keke-beauty-{type}-{DateTime.UtcNow:yyyyMMdd}.csv");}
 private static string C(string? x)=>$"\"{(x??"").Replace("\"","\"\"")}\"";
}
