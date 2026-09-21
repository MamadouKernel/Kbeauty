namespace KekeBeauty.Application.Admin;
public sealed class AdminAccount { public Guid IdAdmin { get; set; } public string Nom { get; set; }=""; public string Email { get; set; }=""; public string MotDePasseHash { get; set; }=""; public string Role { get; set; }=""; public bool Actif { get; set; } }
public sealed class AdminUserItem { public Guid IdUtilisateur { get; set; } public string Nom { get; set; }=""; public string Telephone { get; set; }=""; public string? Email { get; set; } public string TypeCompte { get; set; }=""; public bool EstSuspendu { get; set; } public DateTimeOffset DateCreation { get; set; } }
public sealed class AdminShopItem { public Guid IdEtablissement { get; set; } public string NomEtablissement { get; set; }=""; public string Gerant { get; set; }=""; public string Telephone { get; set; }=""; public string StatutKyc { get; set; }=""; public bool EstSuspendu { get; set; } public DateTimeOffset DateCreation { get; set; } }
public sealed class AdminAuditItem { public Guid IdJournal { get; set; } public string NomAdmin { get; set; }=""; public string Action { get; set; }=""; public string? TypeCible { get; set; } public Guid? IdCible { get; set; } public string? Details { get; set; } public DateTimeOffset DateAction { get; set; } }
public sealed class AdminAccountItem { public Guid IdAdmin {get;set;} public string Nom {get;set;}=""; public string Email {get;set;}=""; public string Role {get;set;}=""; public bool Actif {get;set;} public DateTimeOffset DateCreation {get;set;} public DateTimeOffset? DerniereConnexion {get;set;} }
public interface IAdminManagementRepository {
 Task<AdminAccount?> FindByEmailAsync(string email,CancellationToken ct); Task<int> CountAdminsAsync(CancellationToken ct);
 Task<AdminAccount?> FindByIdAsync(Guid id,CancellationToken ct);
 Task<Guid> CreateAdminAsync(string nom,string email,string passwordHash,string role,CancellationToken ct); Task TouchLoginAsync(Guid id,CancellationToken ct);
 Task<IReadOnlyList<AdminUserItem>> SearchUsersAsync(string? q,CancellationToken ct); Task<IReadOnlyList<AdminShopItem>> SearchShopsAsync(string? q,CancellationToken ct);
 Task AuditAsync(Guid? adminId,string adminName,string action,string? targetType,Guid? targetId,string? details,string? ip,CancellationToken ct); Task<IReadOnlyList<AdminAuditItem>> ListAuditAsync(CancellationToken ct);
 Task<IReadOnlyList<AdminAccountItem>> ListAdminsAsync(CancellationToken ct); Task<bool> SetAdminActiveAsync(Guid id,bool active,CancellationToken ct); Task<bool> UpdateAdminPasswordAsync(Guid id,string passwordHash,CancellationToken ct);
}
