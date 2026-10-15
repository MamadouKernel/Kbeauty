using KekeBeauty.Infrastructure.Persistence;
using KekeBeauty.Modules.Identity.Domain;
using KekeBeauty.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace KekeBeauty.Infrastructure.Admin;

public sealed record AdminUserDto(
    Guid Id,
    string Email,
    string FirstName,
    string LastName,
    IReadOnlyList<string> Roles,
    string Status,
    bool EmailConfirmed,
    bool TotpEnabled,
    DateTimeOffset CreatedAt);

public sealed record UserEstablishmentDto(Guid Id, string Name, string Status);

public sealed record AdminUserDetail(AdminUserDto User, IReadOnlyList<UserEstablishmentDto> Establishments, IReadOnlyList<AuditEntryDto> History);

public sealed record AdminUserFilter(string? Query, string? Role, string? Status);

public sealed record ReasonInput(string? Reason);

public sealed record GrantAdminInput(string? Email);

/// <summary>Accounts: search, suspension with reason (PRD ADM-01) and administrator management (ADM-07).</summary>
public sealed class AdminUserService(KekeBeautyDbContext db, AuditQueryService audit, TimeProvider time)
{
    private static readonly string[] AdminRoles = [AppRoles.Admin, AppRoles.SuperAdmin];

    public async Task<PageOf<AdminUserDto>> ListAsync(AdminUserFilter filter, int? page, int? pageSize, CancellationToken ct)
    {
        var (number, size) = AdminPaging.Normalize(page, pageSize);
        var query = db.Users.AsNoTracking().AsQueryable();
        if (!string.IsNullOrWhiteSpace(filter.Query))
        {
            var like = AdminPaging.Like(filter.Query);
            query = query.Where(u => EF.Functions.ILike(u.Email, like) || EF.Functions.ILike(u.FirstName + " " + u.LastName, like));
        }

        if (!string.IsNullOrWhiteSpace(filter.Role))
        {
            var role = filter.Role;
            query = query.Where(u => u.Roles.Contains(role));
        }

        if (Enum.TryParse<UserStatus>(filter.Status, ignoreCase: true, out var status))
        {
            query = query.Where(u => u.Status == status);
        }

        var total = await query.CountAsync(ct);
        var users = await query.OrderByDescending(u => u.CreatedAt).ThenBy(u => u.Id).Skip((number - 1) * size).Take(size).ToListAsync(ct);
        return new PageOf<AdminUserDto>(users.Select(ToDto).ToList(), total, number, size);
    }

    public async Task<Result<AdminUserDetail>> GetAsync(Guid userId, CancellationToken ct)
    {
        var user = await db.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Id == userId, ct);
        if (user is null)
        {
            return Result.Failure<AdminUserDetail>(AdminErrors.UserNotFound);
        }

        var establishments = await db.Establishments.AsNoTracking().Where(e => e.OwnerUserId == userId)
            .OrderBy(e => e.Name).Select(e => new UserEstablishmentDto(e.Id, e.Name, e.Status.ToString())).ToListAsync(ct);
        var history = await audit.ListAsync(new AuditFilter(null, "User", userId, null, null, null), 1, 20, ct);
        return Result.Success(new AdminUserDetail(ToDto(user), establishments, history.Items));
    }

    public Task<Result> SuspendAsync(AdminContext admin, Guid userId, string? reason, CancellationToken ct) =>
        ChangeStatusAsync(admin, userId, reason, suspend: true, ct);

    public Task<Result> ReactivateAsync(AdminContext admin, Guid userId, string? reason, CancellationToken ct) =>
        ChangeStatusAsync(admin, userId, reason, suspend: false, ct);

    private async Task<Result> ChangeStatusAsync(AdminContext admin, Guid userId, string? rawReason, bool suspend, CancellationToken ct)
    {
        var reason = AdminErrors.CleanReason(rawReason);
        if (reason is null)
        {
            return Result.Failure(AdminErrors.ReasonRequired);
        }

        if (userId == admin.ActorId)
        {
            return Result.Failure(AdminErrors.SelfAction);
        }

        var user = await db.Users.FirstOrDefaultAsync(u => u.Id == userId, ct);
        if (user is null)
        {
            return Result.Failure(AdminErrors.UserNotFound);
        }

        // Admins are managed by a SuperAdmin; a SuperAdmin account can never be suspended from here.
        if (user.Roles.Contains(AppRoles.SuperAdmin) || (user.Roles.Contains(AppRoles.Admin) && !admin.IsSuperAdmin))
        {
            return Result.Failure(AdminErrors.Forbidden);
        }

        if (suspend == (user.Status == UserStatus.Suspended))
        {
            return Result.Failure(AdminErrors.InvalidTransition);
        }

        if (suspend)
        {
            user.Suspend();
            var now = time.GetUtcNow();
            // Sessions end now; the short-lived access token expires on its own.
            await db.RefreshTokens.Where(t => t.UserId == userId && t.RevokedAt == null)
                .ExecuteUpdateAsync(set => set.SetProperty(t => t.RevokedAt, now), ct);
        }
        else
        {
            user.Reactivate();
        }

        Audit.Add(db, admin, time, suspend ? "user.suspend" : "user.reactivate", "User", userId, new { reason });
        await db.SaveChangesAsync(ct);
        return Result.Success();
    }

    // ───────── Administrators (SuperAdmin only, enforced by the endpoint policy) ─────────

    public async Task<IReadOnlyList<AdminUserDto>> ListAdminsAsync(CancellationToken ct)
    {
        var users = await db.Users.AsNoTracking()
            .Where(u => u.Roles.Contains(AppRoles.Admin) || u.Roles.Contains(AppRoles.SuperAdmin))
            .OrderBy(u => u.Email).ToListAsync(ct);
        return users.Select(ToDto).ToList();
    }

    public async Task<Result<AdminUserDto>> GrantAdminAsync(AdminContext admin, string? email, CancellationToken ct)
    {
        var normalized = email?.Trim().ToLowerInvariant();
        if (string.IsNullOrEmpty(normalized))
        {
            return Result.Failure<AdminUserDto>(AdminErrors.Invalid("Indiquez l'adresse e-mail du compte à promouvoir."));
        }

        var user = await db.Users.FirstOrDefaultAsync(u => u.Email == normalized, ct);
        if (user is null)
        {
            return Result.Failure<AdminUserDto>(AdminErrors.UserNotFound);
        }

        if (user.Roles.Any(r => AdminRoles.Contains(r)))
        {
            return Result.Failure<AdminUserDto>(AdminErrors.AlreadyAdmin);
        }

        if (user.Status != UserStatus.Active || !user.EmailConfirmed)
        {
            return Result.Failure<AdminUserDto>(AdminErrors.Invalid("Le compte doit être actif et son adresse e-mail confirmée."));
        }

        user.SetRoles([.. user.Roles, AppRoles.Admin]);
        await RevokeSessionsAsync(user.Id, ct);
        Audit.Add(db, admin, time, "admin.grant", "User", user.Id, new { email = user.Email });
        await db.SaveChangesAsync(ct);
        return Result.Success(ToDto(user));
    }

    public async Task<Result> RevokeAdminAsync(AdminContext admin, Guid userId, CancellationToken ct)
    {
        if (userId == admin.ActorId)
        {
            return Result.Failure(AdminErrors.SelfAction);
        }

        var user = await db.Users.FirstOrDefaultAsync(u => u.Id == userId, ct);
        if (user is null)
        {
            return Result.Failure(AdminErrors.UserNotFound);
        }

        if (user.Roles.Contains(AppRoles.SuperAdmin))
        {
            return Result.Failure(AdminErrors.Forbidden);
        }

        if (!user.Roles.Contains(AppRoles.Admin))
        {
            return Result.Failure(AdminErrors.NotAdmin);
        }

        user.SetRoles(user.Roles.Where(r => r != AppRoles.Admin));
        await RevokeSessionsAsync(user.Id, ct);
        Audit.Add(db, admin, time, "admin.revoke", "User", user.Id, new { email = user.Email });
        await db.SaveChangesAsync(ct);
        return Result.Success();
    }

    /// <summary>Roles live in the access token: forcing a new sign-in makes a role change effective at the next refresh.</summary>
    private async Task RevokeSessionsAsync(Guid userId, CancellationToken ct)
    {
        var now = time.GetUtcNow();
        await db.RefreshTokens.Where(t => t.UserId == userId && t.RevokedAt == null)
            .ExecuteUpdateAsync(set => set.SetProperty(t => t.RevokedAt, now), ct);
    }

    private static AdminUserDto ToDto(User u) =>
        new(u.Id, u.Email, u.FirstName, u.LastName, u.Roles, u.Status.ToString(), u.EmailConfirmed, u.TotpEnabled, u.CreatedAt);
}
