using KekeBeauty.SharedKernel;

namespace KekeBeauty.Modules.Scheduling.Domain;

/// <summary>An employee or workstation that can serve one appointment at a time (PRD AGD-05).</summary>
public sealed class Resource : Entity
{
    public required Guid EstablishmentId { get; set; }
    public required string Name { get; set; }
    public bool IsActive { get; set; } = true;
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

    public DateTimeOffset? FirstReminderSentAt { get; set; }

    public DateTimeOffset? SecondReminderSentAt { get; set; }

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
