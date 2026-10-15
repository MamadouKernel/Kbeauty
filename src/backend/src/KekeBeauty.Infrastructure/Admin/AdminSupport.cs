using System.Text.Json;
using KekeBeauty.Infrastructure.Persistence;
using KekeBeauty.Modules.Administration.Domain;
using KekeBeauty.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace KekeBeauty.Infrastructure.Admin;

/// <summary>Who is acting and from where, for permission checks and the audit trail (PRD ADM-06).</summary>
public sealed record AdminContext(Guid ActorId, bool IsSuperAdmin, string? Ip);

public sealed record PageOf<T>(IReadOnlyList<T> Items, int Total, int Page, int PageSize);

public static class AdminErrors
{
    public static readonly Error UserNotFound = new("user.not_found", "Utilisateur introuvable.");
    public static readonly Error EstablishmentNotFound = new("establishment.not_found", "Établissement introuvable.");
    public static readonly Error MediaNotFound = new("media.not_found", "Média introuvable.");
    public static readonly Error ReasonRequired = new("validation.failed", "Un motif de 3 à 500 caractères est obligatoire.");
    public static readonly Error Forbidden = new("admin.forbidden", "Vous n'avez pas le droit d'effectuer cette action sur ce compte.");
    public static readonly Error SelfAction = new("admin.self_action", "Vous ne pouvez pas appliquer cette action à votre propre compte.");
    public static readonly Error InvalidTransition = new("admin.invalid_transition", "Cette action n'est pas possible dans l'état actuel.");
    public static readonly Error AlreadyAdmin = new("admin.already_admin", "Cet utilisateur est déjà administrateur.");
    public static readonly Error NotAdmin = new("admin.not_admin", "Cet utilisateur n'est pas administrateur.");
    public static readonly Error Taken = new("admin.taken", "Cet élément existe déjà.");

    public static Error Invalid(string message) => new("validation.failed", message);

    /// <summary>The reason a moderator gives is shown to the people concerned and kept in the audit trail.</summary>
    public static string? CleanReason(string? reason)
    {
        var trimmed = reason?.Trim();
        return trimmed is { Length: >= 3 and <= 500 } ? trimmed : null;
    }
}

public static class AdminPaging
{
    public const int MaxPageSize = 100;

    public static (int Page, int Size) Normalize(int? page, int? pageSize) =>
        (Math.Max(page ?? 1, 1), Math.Clamp(pageSize ?? 20, 1, MaxPageSize));

    /// <summary>Escapes a value for LIKE so that a search for "50%" does not become a wildcard.</summary>
    public static string Like(string text) =>
        "%" + text.Trim().Replace("\\", "\\\\", StringComparison.Ordinal).Replace("%", "\\%", StringComparison.Ordinal).Replace("_", "\\_", StringComparison.Ordinal) + "%";
}

public static class Audit
{
    /// <summary>Queues an audit row; the caller saves it together with the change it describes.</summary>
    public static void Add(KekeBeautyDbContext db, AdminContext admin, TimeProvider time, string action, string entityType, Guid entityId, object? data = null) =>
        db.AuditLogs.Add(new AuditLog
        {
            ActorUserId = admin.ActorId,
            Action = action,
            EntityType = entityType,
            EntityId = entityId,
            Data = data is null ? null : JsonSerializer.Serialize(data),
            Ip = admin.Ip,
            At = time.GetUtcNow(),
        });
}

public sealed record AuditEntryDto(
    Guid Id,
    DateTimeOffset At,
    Guid ActorUserId,
    string? ActorEmail,
    string Action,
    string EntityType,
    Guid EntityId,
    string? Data,
    string? Ip);

public sealed record AuditFilter(string? Action, string? EntityType, Guid? EntityId, Guid? ActorId, DateTimeOffset? From, DateTimeOffset? To);

/// <summary>Read side of the audit trail (PRD ADM-06): every admin action and every identity-document view.</summary>
public sealed class AuditQueryService(KekeBeautyDbContext db)
{
    public async Task<PageOf<AuditEntryDto>> ListAsync(AuditFilter filter, int? page, int? pageSize, CancellationToken ct)
    {
        var (number, size) = AdminPaging.Normalize(page, pageSize);
        var query = db.AuditLogs.AsNoTracking().AsQueryable();
        if (!string.IsNullOrWhiteSpace(filter.Action))
        {
            var prefix = filter.Action.Trim();
            query = query.Where(a => a.Action.StartsWith(prefix));
        }

        if (!string.IsNullOrWhiteSpace(filter.EntityType))
        {
            query = query.Where(a => a.EntityType == filter.EntityType);
        }

        if (filter.EntityId is { } entityId)
        {
            query = query.Where(a => a.EntityId == entityId);
        }

        if (filter.ActorId is { } actorId)
        {
            query = query.Where(a => a.ActorUserId == actorId);
        }

        if (filter.From is { } from)
        {
            query = query.Where(a => a.At >= from);
        }

        if (filter.To is { } to)
        {
            query = query.Where(a => a.At < to);
        }

        var total = await query.CountAsync(ct);
        var items = await (
            from a in query.OrderByDescending(a => a.At).ThenByDescending(a => a.Id).Skip((number - 1) * size).Take(size)
            join u in db.Users.AsNoTracking() on a.ActorUserId equals u.Id into actors
            from actor in actors.DefaultIfEmpty()
            orderby a.At descending, a.Id descending
            select new AuditEntryDto(a.Id, a.At, a.ActorUserId, actor == null ? null : actor.Email, a.Action, a.EntityType, a.EntityId, a.Data, a.Ip))
            .ToListAsync(ct);
        return new PageOf<AuditEntryDto>(items, total, number, size);
    }
}
