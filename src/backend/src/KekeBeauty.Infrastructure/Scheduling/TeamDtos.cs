namespace KekeBeauty.Infrastructure.Scheduling;

// ───────── Owner: who works in the establishment ─────────

public sealed record TeamMemberDto(Guid ResourceId, string Name, bool IsActive, Guid? UserId, string? Email, string? CollaboratorName, decimal CommissionPercent);

/// <summary>Links a resource to a collaborator's account (by e-mail) and sets the share paid on each finished service.</summary>
public sealed record TeamAssignmentInput(string? Email, decimal CommissionPercent);

// ───────── Collaborator portal ─────────

public sealed record TeamMembershipDto(Guid ResourceId, string ResourceName, Guid EstablishmentId, string EstablishmentName, decimal CommissionPercent);

public sealed record TeamAppointmentDto(
    Guid Id,
    Guid EstablishmentId,
    string EstablishmentName,
    string ResourceName,
    string ClientName,
    Guid? ClientUserId,
    DateTimeOffset StartsAt,
    DateTimeOffset EndsAt,
    string Status,
    IReadOnlyList<AppointmentLineDto> Lines,
    DateTimeOffset? StartedAt,
    DateTimeOffset? FinishedAt,
    decimal TipAmount,
    string? Allergies,
    bool CanStart,
    bool CanFinish);

public sealed record TeamPlanningDto(string Date, string Timezone, IReadOnlyList<TeamAppointmentDto> Appointments);

public sealed record TechnicalNoteDto(string? Allergies, string? Notes, DateTimeOffset? UpdatedAt);

public sealed record TechnicalNoteInput(string? Allergies, string? Notes);

// ───────── Commissions and tips ─────────

public sealed record CommissionLineDto(Guid AppointmentId, DateTimeOffset FinishedAt, string ClientName, decimal ServicePrice, decimal Commission, decimal Tip);

public sealed record CommissionSummaryDto(
    Guid ResourceId,
    string ResourceName,
    decimal CommissionPercent,
    int Services,
    decimal Revenue,
    decimal Commission,
    decimal Tips,
    decimal Total,
    string CurrencyCode,
    IReadOnlyList<CommissionLineDto> Lines);

public sealed record TipInput(decimal Amount);
