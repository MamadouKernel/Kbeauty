using KekeBeauty.SharedKernel;

namespace KekeBeauty.Modules.Administration.Domain;

/// <summary>Who did what to which record (PRD §8.2 <c>audit_logs</c>). Never holds document contents or secrets.</summary>
public sealed class AuditLog : Entity
{
    public required Guid ActorUserId { get; init; }
    public required string Action { get; init; }
    public required string EntityType { get; init; }
    public required Guid EntityId { get; init; }

    /// <summary>Optional JSON detail (e.g. the decision comment).</summary>
    public string? Data { get; init; }

    public string? Ip { get; init; }
    public DateTimeOffset At { get; init; } = DateTimeOffset.UtcNow;
}
