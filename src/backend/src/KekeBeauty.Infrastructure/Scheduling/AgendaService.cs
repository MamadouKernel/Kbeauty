using KekeBeauty.Infrastructure.Billing;
using KekeBeauty.Infrastructure.Partner;
using KekeBeauty.Infrastructure.Persistence;
using KekeBeauty.Modules.Catalog.Domain;
using KekeBeauty.Modules.Scheduling.Domain;
using KekeBeauty.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace KekeBeauty.Infrastructure.Scheduling;

/// <summary>Partner side of appointments: requests, agenda, booking settings and statistics (PRD AGD-01 to AGD-08).</summary>
public sealed class AgendaService(
    KekeBeautyDbContext db,
    AvailabilityService availability,
    AppointmentWorkflow workflow,
    EntitlementService entitlements,
    TimeProvider time)
{
    private static readonly int[] AllowedSteps = [5, 10, 15, 20, 30, 60];
    private const int MaxMinutesSetting = 7 * 24 * 60;
    private const int MaxListed = 500;

    // ───────── Booking settings ─────────

    public async Task<Result<BookingSettingsDto>> GetSettingsAsync(Guid userId, Guid establishmentId, CancellationToken ct)
    {
        var e = await db.Establishments.AsNoTracking().FirstOrDefaultAsync(x => x.Id == establishmentId && x.OwnerUserId == userId, ct);
        return e is null ? Result.Failure<BookingSettingsDto>(PartnerErrors.NotFound) : Result.Success(ToSettings(e));
    }

    public async Task<Result<BookingSettingsDto>> UpdateSettingsAsync(Guid userId, Guid establishmentId, BookingSettingsDto input, CancellationToken ct)
    {
        var e = await db.Establishments.FirstOrDefaultAsync(x => x.Id == establishmentId && x.OwnerUserId == userId, ct);
        if (e is null)
        {
            return Result.Failure<BookingSettingsDto>(PartnerErrors.NotFound);
        }

        if (!AllowedSteps.Contains(input.SlotStepMinutes)
            || input.MinLeadMinutes is < 0 or > MaxMinutesSetting
            || input.CancelLimitMinutes is < 0 or > MaxMinutesSetting
            || input.MaxHorizonDays is < 1 or > 365
            || input.LateCancelFeePercent is < 0 or > 100)
        {
            return Result.Failure<BookingSettingsDto>(SchedulingErrors.Invalid(
                "Paramètres invalides : intervalle entre créneaux de 5, 10, 15, 20, 30 ou 60 minutes ; horizon de 1 à 365 jours ; délais de 0 à 7 jours ; frais d'annulation tardive de 0 à 100 %."));
        }

        if (input.BookingEnabled)
        {
            var problem = await CannotEnableAsync(e, ct);
            if (problem is not null)
            {
                return Result.Failure<BookingSettingsDto>(SchedulingErrors.Invalid(problem));
            }

            if (!e.BookingAllowed)
            {
                return Result.Failure<BookingSettingsDto>(BillingErrors.FeatureRequired);
            }
        }

        e.BookingEnabled = input.BookingEnabled;
        e.AutoConfirm = input.AutoConfirm;
        e.MinLeadMinutes = input.MinLeadMinutes;
        e.MaxHorizonDays = input.MaxHorizonDays;
        e.CancelLimitMinutes = input.CancelLimitMinutes;
        e.SlotStepMinutes = input.SlotStepMinutes;
        e.LateCancelFeePercent = input.LateCancelFeePercent;
        await db.SaveChangesAsync(ct);
        return Result.Success(ToSettings(e));
    }

    // ───────── Appointments ─────────

    public async Task<Result<IReadOnlyList<PartnerAppointmentDto>>> ListAsync(
        Guid userId,
        Guid establishmentId,
        string? status,
        DateTimeOffset? from,
        DateTimeOffset? to,
        CancellationToken ct)
    {
        if (!await PartnerSupport.OwnedBy(db, userId, establishmentId).AnyAsync(ct))
        {
            return Result.Failure<IReadOnlyList<PartnerAppointmentDto>>(PartnerErrors.NotFound);
        }

        AppointmentStatus? wanted = null;
        if (status is not null)
        {
            if (!Enum.TryParse<AppointmentStatus>(status, ignoreCase: true, out var parsed) || !Enum.IsDefined(parsed))
            {
                return Result.Failure<IReadOnlyList<PartnerAppointmentDto>>(SchedulingErrors.Invalid("Statut inconnu."));
            }

            wanted = parsed;
        }

        var query = db.Appointments.AsNoTracking().Where(a => a.EstablishmentId == establishmentId && !a.AwaitingPayment);
        if (wanted is { } s)
        {
            query = query.Where(a => a.Status == s);
        }

        if (from is { } start)
        {
            query = query.Where(a => a.EndsAt >= start);
        }

        if (to is { } end)
        {
            query = query.Where(a => a.StartsAt <= end);
        }

        var appointments = await query.OrderBy(a => a.StartsAt).Take(MaxListed).ToListAsync(ct);
        return Result.Success(await ProjectAsync(appointments, ct));
    }

    public Task<Result<PartnerAppointmentDto>> ConfirmAsync(Guid userId, Guid appointmentId, CancellationToken ct) =>
        ActAsync(userId, appointmentId, AppointmentStatus.Confirmed, comment: null, ct, a => a.ExpiresAt = null);

    public Task<Result<PartnerAppointmentDto>> RejectAsync(Guid userId, Guid appointmentId, string? reason, CancellationToken ct) =>
        ActAsync(userId, appointmentId, AppointmentStatus.Rejected, reason, ct, a => a.RejectionReason = Trim(reason));

    public Task<Result<PartnerAppointmentDto>> CancelAsync(Guid userId, Guid appointmentId, string? reason, CancellationToken ct) =>
        ActAsync(userId, appointmentId, AppointmentStatus.CancelledByEstablishment, reason, ct, a => a.RejectionReason = Trim(reason));

    /// <summary>How long after the start of an appointment the client may still arrive before she can be marked absent.</summary>
    public static readonly TimeSpan NoShowGrace = TimeSpan.FromMinutes(15);

    public async Task<Result<PartnerAppointmentDto>> NoShowAsync(Guid userId, Guid appointmentId, CancellationToken ct)
    {
        var appointment = await OwnedAppointmentAsync(userId, appointmentId, ct);
        if (appointment is not null && appointment.StartsAt > time.GetUtcNow())
        {
            return Result.Failure<PartnerAppointmentDto>(SchedulingErrors.Invalid("Le rendez-vous n'a pas encore commencé."));
        }

        // Keke Protect: a client running late is not absent yet.
        if (appointment is not null && appointment.StartsAt + NoShowGrace > time.GetUtcNow())
        {
            return Result.Failure<PartnerAppointmentDto>(SchedulingErrors.Invalid($"Attendez {NoShowGrace.TotalMinutes:0} minutes après l'heure du rendez-vous avant de déclarer la cliente absente."));
        }

        return await ActAsync(userId, appointmentId, AppointmentStatus.NoShow, comment: null, ct, _ => { });
    }

    public async Task<Result<PartnerAppointmentDto>> ProposeRescheduleAsync(Guid userId, Guid appointmentId, RescheduleProposal proposal, CancellationToken ct)
    {
        var appointment = await OwnedAppointmentAsync(userId, appointmentId, ct);
        if (appointment is null)
        {
            return Result.Failure<PartnerAppointmentDto>(SchedulingErrors.AppointmentNotFound);
        }

        if (!AppointmentStateMachine.CanTransition(appointment.Status, AppointmentStatus.RescheduleProposed, ActorKind.Partner))
        {
            return Result.Failure<PartnerAppointmentDto>(SchedulingErrors.InvalidTransition);
        }

        // The new slot must be bookable for the same services and the same resource, ignoring this appointment itself.
        var serviceIds = await db.AppointmentLines.AsNoTracking().Where(l => l.AppointmentId == appointment.Id).Select(l => l.ServiceId).ToListAsync(ct);
        var loaded = await availability.LoadContextAsync(appointment.EstablishmentId, serviceIds, appointment.ResourceId, ct);
        if (loaded.IsFailure)
        {
            return Result.Failure<PartnerAppointmentDto>(loaded.Error);
        }

        var free = await availability.FreeResourcesAtAsync(loaded.Value, proposal.StartsAt, appointment.Id, ct);
        if (!free.Contains(appointment.ResourceId))
        {
            return Result.Failure<PartnerAppointmentDto>(SchedulingErrors.SlotUnavailable);
        }

        var from = appointment.Status;
        var applied = workflow.Apply(appointment, AppointmentStatus.RescheduleProposed, ActorKind.Partner, userId, Trim(proposal.Comment));
        if (applied.IsFailure)
        {
            return Result.Failure<PartnerAppointmentDto>(applied.Error);
        }

        appointment.ProposedStartsAt = proposal.StartsAt;
        appointment.ProposedEndsAt = proposal.StartsAt + (appointment.EndsAt - appointment.StartsAt);
        appointment.ExpiresAt = null;
        await db.SaveChangesAsync(ct);
        await workflow.AfterChangeAsync(appointment, from, null, ct);
        return Result.Success((await ProjectAsync([appointment], ct))[0]);
    }

    // ───────── Statistics (AGD-08) ─────────

    public async Task<Result<StatsDto>> StatsAsync(Guid userId, Guid establishmentId, DateOnly from, DateOnly to, CancellationToken ct)
    {
        if (!await PartnerSupport.OwnedBy(db, userId, establishmentId).AnyAsync(ct))
        {
            return Result.Failure<StatsDto>(PartnerErrors.NotFound);
        }

        if (!(await entitlements.EffectiveAsync(establishmentId, ct)).Features.Stats)
        {
            return Result.Failure<StatsDto>(BillingErrors.FeatureRequired);
        }

        if (to < from || to.DayNumber - from.DayNumber > 366)
        {
            return Result.Failure<StatsDto>(SchedulingErrors.Invalid("La période doit couvrir 366 jours au plus."));
        }

        var days = await db.StatsDaily.AsNoTracking().Where(s => s.EstablishmentId == establishmentId && s.Day >= from && s.Day <= to).ToListAsync(ct);
        var start = new DateTimeOffset(from.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero);
        var end = new DateTimeOffset(to.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero).AddDays(1);
        var counts = await db.Appointments.AsNoTracking()
            .Where(a => a.EstablishmentId == establishmentId && !a.AwaitingPayment && a.StartsAt >= start && a.StartsAt < end)
            .GroupBy(a => a.Status)
            .Select(g => new { Status = g.Key, Count = g.Count() })
            .ToListAsync(ct);

        return Result.Success(new StatsDto(
            days.Sum(d => d.Views),
            days.Sum(d => d.CallClicks),
            days.Sum(d => d.DirectionsClicks),
            counts.ToDictionary(c => c.Status.ToString(), c => c.Count)));
    }

    // ───────── Helpers ─────────

    private async Task<Result<PartnerAppointmentDto>> ActAsync(
        Guid userId,
        Guid appointmentId,
        AppointmentStatus to,
        string? comment,
        CancellationToken ct,
        Action<Appointment> mutate)
    {
        var appointment = await OwnedAppointmentAsync(userId, appointmentId, ct);
        if (appointment is null)
        {
            return Result.Failure<PartnerAppointmentDto>(SchedulingErrors.AppointmentNotFound);
        }

        var from = appointment.Status;
        var applied = workflow.Apply(appointment, to, ActorKind.Partner, userId, Trim(comment));
        if (applied.IsFailure)
        {
            return Result.Failure<PartnerAppointmentDto>(applied.Error);
        }

        mutate(appointment);
        appointment.ProposedStartsAt = null;
        appointment.ProposedEndsAt = null;
        await db.SaveChangesAsync(ct);
        await workflow.AfterChangeAsync(appointment, from, null, ct);
        return Result.Success((await ProjectAsync([appointment], ct))[0]);
    }

    public static ServicePaymentInfo? ServicePaymentOf(Appointment a) =>
        a.ServicePaidAt is { } paidAt && a.ServicePaidMethod is { } method ? new ServicePaymentInfo(paidAt, method, a.ServicePaidReference) : null;

    /// <summary>
    /// The establishment records that the client paid it for the service: in cash, or by mobile money with the transaction reference
    /// (required then). Only for appointments that are confirmed or done, and only with a method the establishment accepts.
    /// </summary>
    public async Task<Result<PartnerAppointmentDto>> MarkPaidAsync(Guid userId, Guid appointmentId, MarkPaidInput input, CancellationToken ct)
    {
        var appointment = await OwnedAppointmentAsync(userId, appointmentId, ct);
        if (appointment is null)
        {
            return Result.Failure<PartnerAppointmentDto>(SchedulingErrors.AppointmentNotFound);
        }

        if (appointment.Status is not (AppointmentStatus.Confirmed or AppointmentStatus.Completed))
        {
            return Result.Failure<PartnerAppointmentDto>(SchedulingErrors.Invalid("Seul un rendez-vous confirmé ou terminé peut être marqué comme payé."));
        }

        var establishment = await db.Establishments.AsNoTracking().FirstAsync(e => e.Id == appointment.EstablishmentId, ct);
        var method = input.Method?.Trim();
        var reference = input.Reference?.Trim();
        if (string.Equals(method, "Cash", StringComparison.OrdinalIgnoreCase))
        {
            if (!establishment.AcceptsCash)
            {
                return Result.Failure<PartnerAppointmentDto>(SchedulingErrors.Invalid("Les espèces ne font pas partie de vos moyens de paiement."));
            }

            method = "Cash";
        }
        else if (method is not null && establishment.MobileMoney.Any(m => m.Operator == method))
        {
            if (reference is not { Length: >= 3 and <= 60 })
            {
                return Result.Failure<PartnerAppointmentDto>(SchedulingErrors.Invalid("Indiquez la référence de la transaction Mobile Money (3 à 60 caractères)."));
            }
        }
        else
        {
            return Result.Failure<PartnerAppointmentDto>(SchedulingErrors.Invalid("Moyen de paiement inconnu : choisissez l'un de ceux que vous acceptez."));
        }

        if (reference is { Length: > 60 })
        {
            return Result.Failure<PartnerAppointmentDto>(SchedulingErrors.Invalid("La référence est trop longue (60 caractères au plus)."));
        }

        appointment.ServicePaidAt = time.GetUtcNow();
        appointment.ServicePaidMethod = method;
        appointment.ServicePaidReference = string.IsNullOrEmpty(reference) ? null : reference;
        await db.SaveChangesAsync(ct);
        return Result.Success((await ProjectAsync([appointment], ct))[0]);
    }

    public async Task<Result<PartnerAppointmentDto>> MarkUnpaidAsync(Guid userId, Guid appointmentId, CancellationToken ct)
    {
        var appointment = await OwnedAppointmentAsync(userId, appointmentId, ct);
        if (appointment is null)
        {
            return Result.Failure<PartnerAppointmentDto>(SchedulingErrors.AppointmentNotFound);
        }

        appointment.ServicePaidAt = null;
        appointment.ServicePaidMethod = null;
        appointment.ServicePaidReference = null;
        await db.SaveChangesAsync(ct);
        return Result.Success((await ProjectAsync([appointment], ct))[0]);
    }

    private Task<Appointment?> OwnedAppointmentAsync(Guid userId, Guid appointmentId, CancellationToken ct) =>
        db.Appointments.FirstOrDefaultAsync(
            a => a.Id == appointmentId && db.Establishments.Any(e => e.Id == a.EstablishmentId && e.OwnerUserId == userId),
            ct);

    private async Task<string?> CannotEnableAsync(Establishment e, CancellationToken ct)
    {
        if (e.Status != EstablishmentStatus.Active)
        {
            return "La réservation ne peut être activée qu'une fois l'établissement publié.";
        }

        if (!await db.Services.AnyAsync(s => s.EstablishmentId == e.Id && s.IsBookable && s.IsVisible && s.DeletedAt == null, ct))
        {
            return "Ajoutez au moins une prestation réservable en ligne.";
        }

        if (!await db.OpeningHours.AnyAsync(h => h.EstablishmentId == e.Id, ct))
        {
            return "Renseignez vos horaires d'ouverture.";
        }

        await ResourceService.EnsureDefaultAsync(db, e.Id, time.GetUtcNow(), ct);
        return await db.Resources.AnyAsync(r => r.EstablishmentId == e.Id && r.IsActive, ct)
            ? null
            : "Activez au moins une ressource.";
    }

    private async Task<IReadOnlyList<PartnerAppointmentDto>> ProjectAsync(IReadOnlyList<Appointment> appointments, CancellationToken ct)
    {
        if (appointments.Count == 0)
        {
            return [];
        }

        var clientIds = appointments.Where(a => a.ClientUserId != null).Select(a => a.ClientUserId!.Value).Distinct().ToList();
        var resourceIds = appointments.Select(a => a.ResourceId).Distinct().ToList();
        var appointmentIds = appointments.Select(a => a.Id).ToList();

        var clients = await db.Users.AsNoTracking().Where(u => clientIds.Contains(u.Id))
            .Select(u => new { u.Id, u.FirstName, u.LastName }).ToDictionaryAsync(u => u.Id, ct);
        var resources = await db.Resources.AsNoTracking().Where(r => resourceIds.Contains(r.Id)).ToDictionaryAsync(r => r.Id, r => r.Name, ct);
        var lines = (await db.AppointmentLines.AsNoTracking().Where(l => appointmentIds.Contains(l.AppointmentId)).ToListAsync(ct))
            .GroupBy(l => l.AppointmentId)
            .ToDictionary(g => g.Key, g => (IReadOnlyList<AppointmentLineDto>)g.Select(l => new AppointmentLineDto(l.NameSnapshot, l.PriceSnapshot, l.DurationSnapshot)).ToList());

        return appointments.Select(a =>
        {
            var client = a.ClientUserId is { } clientId ? clients.GetValueOrDefault(clientId) : null;
            return new PartnerAppointmentDto(
                a.Id, a.EstablishmentId, a.ResourceId, resources.GetValueOrDefault(a.ResourceId),
                a.WalkInName ?? (client is null ? "Client" : $"{client.FirstName} {client.LastName}".Trim()),
                a.ClientPhone, a.StartsAt, a.EndsAt, a.Status.ToString(), a.TotalPrice, a.CurrencyCode,
                a.ClientNote, a.RejectionReason, a.ProposedStartsAt, a.ExpiresAt,
                lines.GetValueOrDefault(a.Id) ?? [], a.CreatedAt, ServicePaymentOf(a));
        }).ToList();
    }

    private static BookingSettingsDto ToSettings(Establishment e) =>
        new(e.BookingEnabled, e.AutoConfirm, e.MinLeadMinutes, e.MaxHorizonDays, e.CancelLimitMinutes, e.SlotStepMinutes, e.LateCancelFeePercent);

    private static string? Trim(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim()[..Math.Min(value.Trim().Length, 500)];
}
