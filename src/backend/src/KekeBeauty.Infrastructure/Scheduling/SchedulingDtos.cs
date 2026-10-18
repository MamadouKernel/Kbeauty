namespace KekeBeauty.Infrastructure.Scheduling;

// ───────── Public booking information and availability ─────────

public sealed record BookableService(Guid Id, string Name, string? Description, decimal Price, string PriceType, string CurrencyCode, int DurationMinutes);

public sealed record ResourceDto(Guid Id, string Name, bool IsActive, IReadOnlyList<Guid> ServiceIds);

public sealed record BookingInfo(
    Guid EstablishmentId,
    string Name,
    string Slug,
    string Timezone,
    int MaxHorizonDays,
    int MinLeadMinutes,
    IReadOnlyList<BookableService> Services,
    IReadOnlyList<ResourceDto> Resources);

public sealed record AvailabilitySlot(DateTimeOffset StartsAt, string Time, bool Available);

public sealed record AvailabilityDay(string Date, bool IsOpen, bool HasAvailability, IReadOnlyList<AvailabilitySlot> Slots);

public sealed record AvailabilityResponse(
    string Timezone,
    int DurationMinutes,
    decimal TotalPrice,
    string CurrencyCode,
    IReadOnlyList<AvailabilityDay> Days,
    KekeBeauty.Infrastructure.Billing.PaymentQuote? Payment = null);

// ───────── Appointments ─────────

public sealed record BookingRequest(
    Guid EstablishmentId,
    IReadOnlyList<Guid> ServiceIds,
    Guid? ResourceId,
    DateTimeOffset StartsAt,
    string Phone,
    string? Note);

public sealed record AppointmentLineDto(string Name, decimal Price, int DurationMinutes);

public sealed record AppointmentDto(
    Guid Id,
    Guid EstablishmentId,
    string EstablishmentName,
    string EstablishmentSlug,
    string? ResourceName,
    DateTimeOffset StartsAt,
    DateTimeOffset EndsAt,
    string Status,
    decimal TotalPrice,
    string CurrencyCode,
    string? ClientNote,
    string? RejectionReason,
    DateTimeOffset? ProposedStartsAt,
    DateTimeOffset? ProposedEndsAt,
    DateTimeOffset? ExpiresAt,
    bool CanCancel,
    IReadOnlyList<AppointmentLineDto> Lines,
    DateTimeOffset CreatedAt,
    KekeBeauty.Infrastructure.Billing.AppointmentPaymentDto? Payment = null,
    ServicePaymentInfo? ServicePayment = null,
    decimal TipAmount = 0,
    bool CanTip = false,
    decimal LateCancelFee = 0);

/// <summary>The establishment's record that the service was paid: when, how (Cash or an operator id) and the mobile-money transaction reference.</summary>
public sealed record ServicePaymentInfo(DateTimeOffset PaidAt, string Method, string? Reference);

public sealed record MarkPaidInput(string? Method, string? Reference);

public sealed record PartnerAppointmentDto(
    Guid Id,
    Guid EstablishmentId,
    Guid ResourceId,
    string? ResourceName,
    string ClientName,
    string? ClientPhone,
    DateTimeOffset StartsAt,
    DateTimeOffset EndsAt,
    string Status,
    decimal TotalPrice,
    string CurrencyCode,
    string? ClientNote,
    string? RejectionReason,
    DateTimeOffset? ProposedStartsAt,
    DateTimeOffset? ExpiresAt,
    IReadOnlyList<AppointmentLineDto> Lines,
    DateTimeOffset CreatedAt,
    ServicePaymentInfo? ServicePayment = null);

public sealed record RejectInput(string? Reason);

/// <summary>A client cancel request: after the cancellation limit it only goes through when she accepts the fee.</summary>
public sealed record CancelByClientInput(bool AcceptLateFee);

public sealed record RescheduleProposal(DateTimeOffset StartsAt, string? Comment);

public sealed record CancelInput(string? Reason);

// ───────── Partner configuration ─────────

public sealed record ResourceInput(string Name, IReadOnlyList<Guid> ServiceIds, bool IsActive);

public sealed record TimeOffInput(Guid? ResourceId, DateTimeOffset StartsAt, DateTimeOffset EndsAt, string? Reason);

public sealed record TimeOffDto(Guid Id, Guid? ResourceId, DateTimeOffset StartsAt, DateTimeOffset EndsAt, string? Reason);

public sealed record BookingSettingsDto(
    bool BookingEnabled,
    bool AutoConfirm,
    int MinLeadMinutes,
    int MaxHorizonDays,
    int CancelLimitMinutes,
    int SlotStepMinutes,
    int LateCancelFeePercent = 0);

public sealed record StatsDto(
    int Views,
    int CallClicks,
    int DirectionsClicks,
    IReadOnlyDictionary<string, int> Appointments);

// ───────── Notifications ─────────

public sealed record NotificationDto(Guid Id, string Title, string Body, string TemplateCode, DateTimeOffset CreatedAt, bool IsRead);

public sealed record NotificationList(IReadOnlyList<NotificationDto> Items, int Unread);
