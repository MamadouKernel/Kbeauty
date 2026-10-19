using KekeBeauty.SharedKernel;

namespace KekeBeauty.Modules.Scheduling.Domain;

/// <summary>An employee or workstation that can serve one appointment at a time (PRD AGD-05).</summary>
public sealed class Resource : Entity
{
    public required Guid EstablishmentId { get; set; }
    public required string Name { get; set; }
    public bool IsActive { get; set; } = true;

    /// <summary>The collaborator's account (role PartnerStaff) when the resource is a person who signs in to the team portal.</summary>
    public Guid? UserId { get; set; }

    /// <summary>Share of each finished service's price paid to the collaborator, 0 to 100.</summary>
    public decimal CommissionPercent { get; set; }

    public DateTimeOffset CreatedAt { get; init; } = DateTimeOffset.UtcNow;
}

/// <summary>Services a resource performs. A resource without any row performs every service.</summary>
public sealed class ResourceServiceLink
{
    public Guid ResourceId { get; init; }

    /// <summary>Plain identifier: the Catalog module is never navigated from here.</summary>
    public Guid ServiceId { get; init; }
}

/// <summary>Closure of the whole establishment (<see cref="ResourceId"/> null) or of one resource (PRD AGD-04).</summary>
public sealed class TimeOff : Entity
{
    public required Guid EstablishmentId { get; set; }
    public Guid? ResourceId { get; set; }
    public required DateTimeOffset StartsAt { get; set; }
    public required DateTimeOffset EndsAt { get; set; }
    public string? Reason { get; set; }
}

public enum AppointmentStatus
{
    Pending,
    Confirmed,
    Rejected,
    Expired,
    CancelledByClient,
    CancelledByEstablishment,
    RescheduleProposed,
    Completed,
    NoShow,
}

public sealed class Appointment : Entity
{
    public required Guid EstablishmentId { get; set; }
    /// <summary>Null for a walk-in recorded at the counter by the establishment: the client has no account.</summary>
    public Guid? ClientUserId { get; set; }

    /// <summary>Name given at the counter; only set for a walk-in.</summary>
    public string? WalkInName { get; set; }

    public required Guid ResourceId { get; set; }
    public required DateTimeOffset StartsAt { get; set; }
    public required DateTimeOffset EndsAt { get; set; }
    public AppointmentStatus Status { get; set; } = AppointmentStatus.Pending;
    public decimal TotalPrice { get; set; }
    public string CurrencyCode { get; set; } = "XOF";
    public string? ClientNote { get; set; }

    /// <summary>Contact number given at booking time, used for SMS only.</summary>
    public string? ClientPhone { get; set; }

    public string? RejectionReason { get; set; }
    public DateTimeOffset? ProposedStartsAt { get; set; }
    public DateTimeOffset? ProposedEndsAt { get; set; }

    /// <summary>Deadline for the partner to answer a <see cref="AppointmentStatus.Pending"/> request.</summary>
    public DateTimeOffset? ExpiresAt { get; set; }

    /// <summary>When the first and the second automatic reminders were sent (null while not yet sent); each is sent at most once.</summary>
    /// <summary>True while the client still has to pay the deposit: the slot is held, but the partner does not see the request yet.</summary>
    public bool AwaitingPayment { get; set; }

    /// <summary>
    /// The establishment recorded that the client paid for the service (cash, or mobile money with the transaction reference).
    /// The platform does not collect this money; this is the establishment's own bookkeeping, shown to the client too.
    /// </summary>
    public DateTimeOffset? ServicePaidAt { get; set; }

    /// <summary><c>Cash</c> or the mobile-money operator id the client paid with.</summary>
    public string? ServicePaidMethod { get; set; }

    public string? ServicePaidReference { get; set; }

    /// <summary>Service timer of the team portal: when the collaborator started and finished the treatment.</summary>
    public DateTimeOffset? StartedAt { get; set; }

    public DateTimeOffset? FinishedAt { get; set; }

    /// <summary>Tip the client left for the collaborator after the treatment (0 when none).</summary>
    public decimal TipAmount { get; set; }

    /// <summary>
    /// What the collaborator earned on this service and who she was, frozen when the service was done: a later change of rate or of
    /// person never rewrites history. Null until then (older rows fall back to the resource current rate).
    /// </summary>
    public decimal? CommissionAmount { get; set; }

    public Guid? CommissionUserId { get; set; }

    /// <summary>Set once the commission has been paid to the collaborator (<c>billing.collaborator_payouts</c>).</summary>
    public Guid? CollaboratorPayoutId { get; set; }

    public DateTimeOffset? FirstReminderSentAt { get; set; }

    public DateTimeOffset? SecondReminderSentAt { get; set; }

    /// <summary>When the client was invited to come back some weeks after this visit (sent at most once).</summary>
    public DateTimeOffset? ReengagementSentAt { get; set; }

    public DateTimeOffset CreatedAt { get; init; } = DateTimeOffset.UtcNow;
}

/// <summary>Snapshot of a booked service: later price or name changes never rewrite history (PRD BIL-03 spirit).</summary>
public sealed class AppointmentLine : Entity
{
    public required Guid AppointmentId { get; init; }
    public required Guid ServiceId { get; init; }
    public required string NameSnapshot { get; init; }
    public required decimal PriceSnapshot { get; init; }
    public required int DurationSnapshot { get; init; }
}

public sealed class AppointmentStatusHistory : Entity
{
    public required Guid AppointmentId { get; init; }
    public AppointmentStatus? FromStatus { get; init; }
    public required AppointmentStatus ToStatus { get; init; }
    public Guid? ActorUserId { get; init; }
    public DateTimeOffset At { get; init; } = DateTimeOffset.UtcNow;
    public string? Comment { get; init; }
}

public enum DisputeReason
{
    SalonClosed,
    LateOrAbsent,
    ServiceNotAsDescribed,
    Other,
}

public enum DisputeStatus
{
    Open,
    Resolved,
}

public enum DisputeResolution
{
    /// <summary>The claim is upheld: the client is refunded what she paid online.</summary>
    RefundedClient,

    /// <summary>The claim is dismissed: the establishment keeps what it earned.</summary>
    RejectedClaim,
}

/// <summary>
/// A client claim about a past appointment (Keke Protect). While it is open, the money the platform holds for that
/// appointment is not paid out to the establishment. One claim per appointment.
/// </summary>
public sealed class Dispute : Entity
{
    public required Guid AppointmentId { get; init; }
    public required Guid EstablishmentId { get; init; }
    public required Guid ClientUserId { get; init; }
    public required DisputeReason Reason { get; init; }
    public required string Description { get; init; }
    public DisputeStatus Status { get; set; } = DisputeStatus.Open;
    public DisputeResolution? Resolution { get; set; }
    public string? PartnerResponse { get; set; }
    public DateTimeOffset? PartnerRespondedAt { get; set; }
    public string? AdminNote { get; set; }
    public Guid? ResolvedBy { get; set; }
    public DateTimeOffset? ResolvedAt { get; set; }
    public DateTimeOffset CreatedAt { get; init; } = DateTimeOffset.UtcNow;
}

/// <summary>Treatment notes of a client in one establishment: allergies and what to remember next time.</summary>
public sealed class ClientTechnicalNote : Entity
{
    public required Guid EstablishmentId { get; init; }
    public required Guid ClientUserId { get; init; }
    public string? Allergies { get; set; }
    public string? Notes { get; set; }
    public Guid UpdatedByUserId { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}
