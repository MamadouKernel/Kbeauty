using KekeBeauty.Infrastructure.Partner;
using KekeBeauty.Infrastructure.Persistence;
using KekeBeauty.Modules.Scheduling.Domain;
using KekeBeauty.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace KekeBeauty.Infrastructure.Scheduling;

/// <summary>Client side of appointments: booking, listing, cancelling and answering a reschedule proposal (PRD RDV-03/05/06).</summary>
public sealed class BookingService(
    KekeBeautyDbContext db,
    AvailabilityService availability,
    AppointmentWorkflow workflow,
    KekeBeauty.Infrastructure.Billing.AppointmentPaymentService payments,
    KekeBeauty.Infrastructure.Billing.EntitlementService entitlements,
    TimeProvider time)
{
    public const int MaxNoteLength = 500;

    public async Task<Result<AppointmentDto>> CreateAsync(Guid clientId, BookingRequest request, CancellationToken ct)
    {
        using var activity = Telemetry.Source.StartActivity("booking.create");
        activity?.SetTag("establishment.id", request.EstablishmentId);
        var phone = PartnerText.NormalizePhone(request.Phone);
        if (phone is null)
        {
            return Result.Failure<AppointmentDto>(SchedulingErrors.Invalid("Numéro de téléphone invalide : il sert à vous envoyer le SMS de confirmation."));
        }

        var note = string.IsNullOrWhiteSpace(request.Note) ? null : request.Note.Trim();
        if (note is { Length: > MaxNoteLength })
        {
            return Result.Failure<AppointmentDto>(SchedulingErrors.Invalid($"La note est limitée à {MaxNoteLength} caractères."));
        }

        var loaded = await availability.LoadContextAsync(request.EstablishmentId, request.ServiceIds, request.ResourceId, ct);
        if (loaded.IsFailure)
        {
            return Result.Failure<AppointmentDto>(loaded.Error);
        }

        var context = loaded.Value;
        var free = await availability.FreeResourcesAtAsync(context, request.StartsAt, null, ct);
        if (free.Count == 0)
        {
            return Result.Failure<AppointmentDto>(SchedulingErrors.SlotUnavailable);
        }

        // The plan of the establishment may cap the appointments it takes in a month.
        if ((await entitlements.EffectiveAsync(context.Establishment.Id, ct)).Features.MaxAppointmentsPerMonth is { } monthlyLimit)
        {
            var monthStart = new DateTimeOffset(request.StartsAt.Year, request.StartsAt.Month, 1, 0, 0, 0, TimeSpan.Zero);
            var counted = await db.Appointments.CountAsync(a => a.EstablishmentId == context.Establishment.Id
                && a.StartsAt >= monthStart && a.StartsAt < monthStart.AddMonths(1)
                && (a.Status == AppointmentStatus.Pending || a.Status == AppointmentStatus.Confirmed
                    || a.Status == AppointmentStatus.RescheduleProposed || a.Status == AppointmentStatus.Completed), ct);
            if (counted >= monthlyLimit)
            {
                return Result.Failure<AppointmentDto>(new Error("booking.limit_reached", "Cet établissement ne peut plus accepter de rendez-vous pour ce mois-ci. Choisissez un autre mois."));
            }
        }

        // Prevent a single client from hoarding all slots of a salon day.
        var dayStart = new DateTimeOffset(request.StartsAt.UtcDateTime.Date, TimeSpan.Zero);
        var clientDailyCount = await db.Appointments.CountAsync(a => a.EstablishmentId == context.Establishment.Id
            && a.ClientUserId == clientId
            && a.StartsAt >= dayStart && a.StartsAt < dayStart.AddDays(1)
            && (a.Status == AppointmentStatus.Pending || a.Status == AppointmentStatus.Confirmed
                || a.Status == AppointmentStatus.RescheduleProposed), ct);
        if (clientDailyCount >= 4)
        {
            return Result.Failure<AppointmentDto>(new Error("booking.daily_limit_reached", "Vous avez atteint le nombre maximal de réservations pour cette journée dans cet établissement."));
        }

        var now = time.GetUtcNow();
        var plan = await payments.PlanAsync(context.Establishment, context.TotalPrice, context.Services[0].CurrencyCode, ct);

        // With an online payment the slot is only held: the appointment waits (unseen by the establishment) until the money is in.
        var status = plan is null && context.Establishment.AutoConfirm ? AppointmentStatus.Confirmed : AppointmentStatus.Pending;
        Appointment? created = null;

        // "No preference" tries every free resource: another client may take one between our check and the insert.
        foreach (var resourceId in free)
        {
            var appointment = new Appointment
            {
                EstablishmentId = context.Establishment.Id,
                ClientUserId = clientId,
                ResourceId = resourceId,
                StartsAt = request.StartsAt,
                EndsAt = request.StartsAt.AddMinutes(context.DurationMinutes),
                Status = status,
                TotalPrice = context.TotalPrice,
                CurrencyCode = context.Services[0].CurrencyCode,
                ClientNote = note,
                ClientPhone = phone,
                ExpiresAt = plan is not null
                    ? now.AddMinutes(KekeBeauty.Infrastructure.Billing.AppointmentPaymentService.HoldMinutes)
                    : status == AppointmentStatus.Pending ? now.AddHours(AppointmentWorkflow.RequestValidityHours) : null,
                AwaitingPayment = plan is not null,
                CreatedAt = now,
            };
            db.Appointments.Add(appointment);
            foreach (var service in context.Services)
            {
                db.AppointmentLines.Add(new AppointmentLine
                {
                    AppointmentId = appointment.Id,
                    ServiceId = service.Id,
                    NameSnapshot = service.Name,
                    PriceSnapshot = service.EffectivePrice(now),
                    DurationSnapshot = service.DurationMinutes,
                });
            }

            db.AppointmentHistory.Add(new AppointmentStatusHistory
            {
                AppointmentId = appointment.Id,
                ToStatus = status,
                ActorUserId = clientId,
                At = now,
            });

            try
            {
                await db.SaveChangesAsync(ct);
                created = appointment;
                break;
            }
            catch (Exception ex) when (AppointmentWorkflow.IsOverlapViolation(ex))
            {
                db.ChangeTracker.Clear();
            }
        }

        if (created is null)
        {
            return Result.Failure<AppointmentDto>(SchedulingErrors.SlotTaken);
        }

        if (plan is not null)
        {
            var started = await payments.StartAsync(created, plan, context.Establishment.Name, ct);
            if (started.IsFailure)
            {
                // The payment page could not be opened: release the slot instead of leaving a hold nobody can pay.
                workflow.Apply(created, AppointmentStatus.Expired, ActorKind.System, null, "Paiement indisponible");
                await db.SaveChangesAsync(ct);
                await workflow.AfterChangeAsync(created, AppointmentStatus.Pending, null, ct);
                return Result.Failure<AppointmentDto>(started.Error);
            }
        }

        await workflow.AfterChangeAsync(created, null, null, ct);
        Telemetry.BookingsCreated.Add(1);
        return Result.Success((await ProjectAsync([created], ct))[0]);
    }

    /// <summary>The client comes back to a held appointment to pay it.</summary>
    public async Task<Result<AppointmentDto>> ResumePaymentAsync(Guid clientId, Guid id, CancellationToken ct)
    {
        var resumed = await payments.ResumeAsync(clientId, id, ct);
        if (resumed.IsFailure)
        {
            return Result.Failure<AppointmentDto>(resumed.Error);
        }

        var appointment = await db.Appointments.AsNoTracking().FirstAsync(a => a.Id == id, ct);
        return Result.Success((await ProjectAsync([appointment], ct))[0]);
    }

    public async Task<IReadOnlyList<AppointmentDto>> ListMineAsync(Guid clientId, CancellationToken ct)
    {
        var appointments = await db.Appointments.AsNoTracking()
            .Where(a => a.ClientUserId == clientId)
            .OrderByDescending(a => a.StartsAt)
            .Take(200)
            .ToListAsync(ct);
        return await ProjectAsync(appointments, ct);
    }

    public async Task<Result<AppointmentDto>> CancelAsync(Guid clientId, Guid id, bool acceptLateFee, CancellationToken ct)
    {
        var appointment = await db.Appointments.FirstOrDefaultAsync(a => a.Id == id && a.ClientUserId == clientId, ct);
        if (appointment is null)
        {
            return Result.Failure<AppointmentDto>(SchedulingErrors.AppointmentNotFound);
        }

        KekeBeauty.Modules.Billing.Domain.AppointmentPayment? lateSplit = null;
        if (appointment.Status != AppointmentStatus.RescheduleProposed)
        {
            var rules = await db.Establishments.AsNoTracking().Where(e => e.Id == appointment.EstablishmentId)
                .Select(e => new { e.CancelLimitMinutes, e.LateCancelFeePercent }).FirstAsync(ct);
            if (AppointmentStateMachine.IsActive(appointment.Status) && appointment.StartsAt - time.GetUtcNow() < TimeSpan.FromMinutes(rules.CancelLimitMinutes))
            {
                // Late: only possible when the establishment keeps a fee out of an online payment, and the client accepts it.
                var retention = await payments.LateRetentionAsync(appointment, rules.LateCancelFeePercent, ct);
                // A fee only makes sense before the visit: once it has started, the usual refusal applies.
                if (retention is null || appointment.StartsAt <= time.GetUtcNow() || appointment.StartedAt != null)
                {
                    return Result.Failure<AppointmentDto>(SchedulingErrors.TooLate);
                }

                if (!acceptLateFee)
                {
                    return Result.Failure<AppointmentDto>(SchedulingErrors.FeeNotAccepted(retention.Fee));
                }

                retention.Record.RefundAmount = retention.Refund;
                retention.Record.RetainedNet = retention.RetainedNet;
                await db.SaveChangesAsync(ct);
                lateSplit = retention.Record;
            }
        }

        try
        {
            var cancelled = await TransitionAsync(appointment, AppointmentStatus.CancelledByClient, clientId, comment: null, ct);
            if (cancelled.IsFailure && lateSplit is not null)
            {
                await ClearLateSplitAsync(lateSplit.Id);
            }

            return cancelled;
        }
        catch when (lateSplit is not null)
        {
            // The cancellation did not go through: the fee must not stay on the payment.
            await ClearLateSplitAsync(lateSplit.Id);
            throw;
        }
    }

    private Task ClearLateSplitAsync(Guid paymentId) =>
        db.AppointmentPayments.Where(p => p.Id == paymentId)
            .ExecuteUpdateAsync(set => set.SetProperty(p => p.RefundAmount, (decimal?)null).SetProperty(p => p.RetainedNet, (decimal?)null));

    public const int MaxClientReschedules = 2;
    public const string ClientMovedComment = "Déplacé par le client";
    public const string LateCommentPrefix = "Retard signalé : ";
    public static readonly int[] LateChoices = [10, 15, 30];

    /// <summary>
    /// The client moves an appointment to another free slot (V1 "reprogrammer"). Same cut-off as a cancellation, at most
    /// <see cref="MaxClientReschedules"/> moves, same services and duration; the establishment is told and may still cancel.
    /// </summary>
    public async Task<Result<AppointmentDto>> RescheduleAsync(Guid clientId, Guid id, DateTimeOffset newStart, CancellationToken ct)
    {
        var appointment = await db.Appointments.FirstOrDefaultAsync(a => a.Id == id && a.ClientUserId == clientId, ct);
        if (appointment is null)
        {
            return Result.Failure<AppointmentDto>(SchedulingErrors.AppointmentNotFound);
        }

        var now = time.GetUtcNow();
        if (appointment.Status is not (AppointmentStatus.Pending or AppointmentStatus.Confirmed) || appointment.AwaitingPayment || appointment.StartedAt != null)
        {
            return Result.Failure<AppointmentDto>(SchedulingErrors.InvalidTransition);
        }

        if (newStart <= now || newStart == appointment.StartsAt)
        {
            return Result.Failure<AppointmentDto>(SchedulingErrors.SlotUnavailable);
        }

        var cancelLimit = await db.Establishments.AsNoTracking().Where(e => e.Id == appointment.EstablishmentId)
            .Select(e => e.CancelLimitMinutes).FirstAsync(ct);
        if (appointment.StartsAt - now < TimeSpan.FromMinutes(cancelLimit))
        {
            return Result.Failure<AppointmentDto>(SchedulingErrors.TooLate);
        }

        var moves = await db.AppointmentHistory.CountAsync(h => h.AppointmentId == appointment.Id && h.Comment == ClientMovedComment, ct);
        if (moves >= MaxClientReschedules)
        {
            return Result.Failure<AppointmentDto>(SchedulingErrors.RescheduleLimit);
        }

        var serviceIds = await db.AppointmentLines.AsNoTracking().Where(l => l.AppointmentId == appointment.Id).Select(l => l.ServiceId).ToListAsync(ct);
        var loaded = await availability.LoadContextAsync(appointment.EstablishmentId, serviceIds, null, ct);
        if (loaded.IsFailure)
        {
            return Result.Failure<AppointmentDto>(loaded.Error);
        }

        var free = await availability.FreeResourcesAtAsync(loaded.Value, newStart, appointment.Id, ct);
        if (free.Count == 0)
        {
            return Result.Failure<AppointmentDto>(SchedulingErrors.SlotUnavailable);
        }

        var previous = appointment.StartsAt;
        var duration = appointment.EndsAt - appointment.StartsAt;
        appointment.ResourceId = free.Contains(appointment.ResourceId) ? appointment.ResourceId : free[0];
        appointment.StartsAt = newStart;
        appointment.EndsAt = newStart + duration;
        if (appointment.Status == AppointmentStatus.Pending && appointment.ExpiresAt != null)
        {
            appointment.ExpiresAt = now.AddHours(AppointmentWorkflow.RequestValidityHours);
        }

        db.AppointmentHistory.Add(new AppointmentStatusHistory
        {
            AppointmentId = appointment.Id,
            FromStatus = appointment.Status,
            ToStatus = appointment.Status,
            ActorUserId = clientId,
            At = now,
            Comment = ClientMovedComment,
        });

        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (Exception ex) when (AppointmentWorkflow.IsOverlapViolation(ex))
        {
            db.ChangeTracker.Clear();
            return Result.Failure<AppointmentDto>(SchedulingErrors.SlotTaken);
        }

        await workflow.AfterChangeAsync(appointment, appointment.Status, previous, ct);
        return Result.Success((await ProjectAsync([appointment], ct))[0]);
    }

    /// <summary>The client tells the establishment they are running late (V1 "retard"): once per appointment, around the visit only.</summary>
    public async Task<Result<AppointmentDto>> SignalLateAsync(Guid clientId, Guid id, int minutes, CancellationToken ct)
    {
        if (!LateChoices.Contains(minutes))
        {
            return Result.Failure<AppointmentDto>(SchedulingErrors.Invalid("Choisissez un retard de 10, 15 ou 30 minutes."));
        }

        var appointment = await db.Appointments.FirstOrDefaultAsync(a => a.Id == id && a.ClientUserId == clientId, ct);
        if (appointment is null)
        {
            return Result.Failure<AppointmentDto>(SchedulingErrors.AppointmentNotFound);
        }

        var now = time.GetUtcNow();
        if (appointment.Status != AppointmentStatus.Confirmed || appointment.StartedAt != null
            || now < appointment.StartsAt.AddHours(-3) || now > appointment.StartsAt.AddMinutes(30))
        {
            return Result.Failure<AppointmentDto>(SchedulingErrors.InvalidTransition);
        }

        if (await db.AppointmentHistory.AnyAsync(h => h.AppointmentId == appointment.Id && h.Comment != null && h.Comment.StartsWith(LateCommentPrefix), ct))
        {
            return Result.Failure<AppointmentDto>(SchedulingErrors.Invalid("Vous avez déjà signalé un retard pour ce rendez-vous."));
        }

        db.AppointmentHistory.Add(new AppointmentStatusHistory
        {
            AppointmentId = appointment.Id,
            FromStatus = appointment.Status,
            ToStatus = appointment.Status,
            ActorUserId = clientId,
            At = now,
            Comment = $"{LateCommentPrefix}{minutes} min",
        });
        await db.SaveChangesAsync(ct);

        await workflow.NotifyOwnerAsync(
            appointment,
            "appointment.client_late.partner",
            "Retard signalé",
            $"Retard d'environ {minutes} minutes signalé pour ce rendez-vous.",
            new Dictionary<string, string> { ["minutes"] = minutes.ToString(System.Globalization.CultureInfo.InvariantCulture) },
            ct);
        return Result.Success((await ProjectAsync([appointment], ct))[0]);
    }

    public async Task<Result<AppointmentDto>> AcceptRescheduleAsync(Guid clientId, Guid id, CancellationToken ct)
    {
        var appointment = await db.Appointments.FirstOrDefaultAsync(a => a.Id == id && a.ClientUserId == clientId, ct);
        if (appointment is null)
        {
            return Result.Failure<AppointmentDto>(SchedulingErrors.AppointmentNotFound);
        }

        if (appointment.Status != AppointmentStatus.RescheduleProposed
            || appointment.ProposedStartsAt is not { } start
            || appointment.ProposedEndsAt is not { } end)
        {
            return Result.Failure<AppointmentDto>(SchedulingErrors.InvalidTransition);
        }

        if (start <= time.GetUtcNow())
        {
            return Result.Failure<AppointmentDto>(SchedulingErrors.SlotUnavailable);
        }

        var previous = appointment.StartsAt;
        var applied = workflow.Apply(appointment, AppointmentStatus.Confirmed, ActorKind.Client, clientId, "Reprogrammation acceptée");
        if (applied.IsFailure)
        {
            return Result.Failure<AppointmentDto>(applied.Error);
        }

        appointment.StartsAt = start;
        appointment.EndsAt = end;
        appointment.ProposedStartsAt = null;
        appointment.ProposedEndsAt = null;
        appointment.ExpiresAt = null;

        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (Exception ex) when (AppointmentWorkflow.IsOverlapViolation(ex))
        {
            db.ChangeTracker.Clear();
            return Result.Failure<AppointmentDto>(SchedulingErrors.SlotTaken);
        }

        await workflow.AfterChangeAsync(appointment, AppointmentStatus.RescheduleProposed, previous, ct);
        return Result.Success((await ProjectAsync([appointment], ct))[0]);
    }

    public async Task<Result<AppointmentDto>> DeclineRescheduleAsync(Guid clientId, Guid id, CancellationToken ct)
    {
        var appointment = await db.Appointments.FirstOrDefaultAsync(a => a.Id == id && a.ClientUserId == clientId, ct);
        if (appointment is null)
        {
            return Result.Failure<AppointmentDto>(SchedulingErrors.AppointmentNotFound);
        }

        if (appointment.Status != AppointmentStatus.RescheduleProposed)
        {
            return Result.Failure<AppointmentDto>(SchedulingErrors.InvalidTransition);
        }

        return await TransitionAsync(appointment, AppointmentStatus.CancelledByClient, clientId, "Reprogrammation refusée", ct);
    }

    public async Task<Result<string>> CalendarIcsAsync(Guid clientId, Guid id, CancellationToken ct)
    {
        var appointment = await db.Appointments.AsNoTracking().FirstOrDefaultAsync(a => a.Id == id && a.ClientUserId == clientId, ct);
        if (appointment is null)
        {
            return Result.Failure<string>(SchedulingErrors.AppointmentNotFound);
        }

        var establishment = await db.Establishments.AsNoTracking()
            .Where(e => e.Id == appointment.EstablishmentId)
            .Select(e => new { e.Name, Address = e.AddressLine, Phone = e.CustomerServicePhone })
            .FirstOrDefaultAsync(ct);
        if (establishment is null)
        {
            return Result.Failure<string>(SchedulingErrors.AppointmentNotFound);
        }

        var lines = await db.AppointmentLines.AsNoTracking()
            .Where(l => l.AppointmentId == appointment.Id)
            .Select(l => l.NameSnapshot)
            .ToListAsync(ct);
        var resourceName = await db.Resources.AsNoTracking()
            .Where(r => r.Id == appointment.ResourceId)
            .Select(r => r.Name)
            .FirstOrDefaultAsync(ct);

        static string EscapeIcs(string? value) =>
            string.IsNullOrWhiteSpace(value)
                ? string.Empty
                : value.Replace("\\", "\\\\", StringComparison.Ordinal)
                    .Replace(";", "\\;", StringComparison.Ordinal)
                    .Replace(",", "\\,", StringComparison.Ordinal)
                    .Replace("\r\n", "\\n", StringComparison.Ordinal)
                    .Replace("\n", "\\n", StringComparison.Ordinal);

        var locationParts = new[] { establishment.Name, establishment.Address }
            .Where(s => !string.IsNullOrWhiteSpace(s));
        var descriptionParts = new List<string>
        {
            $"Prestations : {string.Join(", ", lines)}",
            $"Total : {appointment.TotalPrice:0} {appointment.CurrencyCode}",
        };
        if (!string.IsNullOrWhiteSpace(resourceName))
        {
            descriptionParts.Add($"Intervenant(e) : {resourceName}");
        }

        if (!string.IsNullOrWhiteSpace(establishment.Phone))
        {
            descriptionParts.Add($"Téléphone : {establishment.Phone}");
        }

        var nowUtc = time.GetUtcNow().UtcDateTime.ToString("yyyyMMdd'T'HHmmss'Z'", System.Globalization.CultureInfo.InvariantCulture);
        var startUtc = appointment.StartsAt.UtcDateTime.ToString("yyyyMMdd'T'HHmmss'Z'", System.Globalization.CultureInfo.InvariantCulture);
        var endUtc = appointment.EndsAt.UtcDateTime.ToString("yyyyMMdd'T'HHmmss'Z'", System.Globalization.CultureInfo.InvariantCulture);

        var ics = string.Join("\r\n", [
            "BEGIN:VCALENDAR",
            "VERSION:2.0",
            "PRODID:-//Keke Beauty//RDV//FR",
            "CALSCALE:GREGORIAN",
            "METHOD:PUBLISH",
            "BEGIN:VEVENT",
            $"UID:rdv-{appointment.Id}@kekebeauty.ci",
            $"DTSTAMP:{nowUtc}",
            $"DTSTART:{startUtc}",
            $"DTEND:{endUtc}",
            $"SUMMARY:{EscapeIcs($"RDV Keke Beauty — {establishment.Name}")}",
            $"LOCATION:{EscapeIcs(string.Join(" — ", locationParts))}",
            $"DESCRIPTION:{EscapeIcs(string.Join("\n", descriptionParts))}",
            "BEGIN:VALARM",
            "TRIGGER:-PT2H",
            "ACTION:DISPLAY",
            $"DESCRIPTION:{EscapeIcs($"Rappel RDV chez {establishment.Name}")}",
            "END:VALARM",
            "END:VEVENT",
            "END:VCALENDAR",
            string.Empty,
        ]);

        return Result.Success(ics);
    }

    private async Task<Result<AppointmentDto>> TransitionAsync(Appointment appointment, AppointmentStatus to, Guid clientId, string? comment, CancellationToken ct)
    {
        var from = appointment.Status;
        var applied = workflow.Apply(appointment, to, ActorKind.Client, clientId, comment);
        if (applied.IsFailure)
        {
            return Result.Failure<AppointmentDto>(applied.Error);
        }

        appointment.ProposedStartsAt = null;
        appointment.ProposedEndsAt = null;
        await db.SaveChangesAsync(ct);
        await workflow.AfterChangeAsync(appointment, from, null, ct);
        return Result.Success((await ProjectAsync([appointment], ct))[0]);
    }

    private async Task<IReadOnlyList<AppointmentDto>> ProjectAsync(IReadOnlyList<Appointment> appointments, CancellationToken ct)
    {
        if (appointments.Count == 0)
        {
            return [];
        }

        var establishmentIds = appointments.Select(a => a.EstablishmentId).Distinct().ToList();
        var resourceIds = appointments.Select(a => a.ResourceId).Distinct().ToList();
        var appointmentIds = appointments.Select(a => a.Id).ToList();

        var establishments = await db.Establishments.AsNoTracking().Where(e => establishmentIds.Contains(e.Id))
            .Select(e => new { e.Id, e.Name, e.Slug, e.CancelLimitMinutes, e.LateCancelFeePercent })
            .ToDictionaryAsync(e => e.Id, ct);
        var resources = await db.Resources.AsNoTracking().Where(r => resourceIds.Contains(r.Id))
            .ToDictionaryAsync(r => r.Id, r => r.Name, ct);
        var lines = (await db.AppointmentLines.AsNoTracking().Where(l => appointmentIds.Contains(l.AppointmentId)).ToListAsync(ct))
            .GroupBy(l => l.AppointmentId)
            .ToDictionary(g => g.Key, g => (IReadOnlyList<AppointmentLineDto>)g.Select(l => new AppointmentLineDto(l.NameSnapshot, l.PriceSnapshot, l.DurationSnapshot, l.ServiceId)).ToList());

        var now = time.GetUtcNow();
        var moves = await db.AppointmentHistory.AsNoTracking()
            .Where(h => appointmentIds.Contains(h.AppointmentId) && h.Comment == ClientMovedComment)
            .GroupBy(h => h.AppointmentId).Select(g => new { g.Key, Count = g.Count() })
            .ToDictionaryAsync(g => g.Key, g => g.Count, ct);
        var signalled = (await db.AppointmentHistory.AsNoTracking()
            .Where(h => appointmentIds.Contains(h.AppointmentId) && h.Comment != null && h.Comment.StartsWith(LateCommentPrefix))
            .Select(h => h.AppointmentId).ToListAsync(ct)).ToHashSet();
        var paymentInfo = await payments.InfoForAsync(appointments, ct);
        var staffed = await db.Resources.AsNoTracking().Where(r => resourceIds.Contains(r.Id) && r.UserId != null).Select(r => r.Id).ToListAsync(ct);
        var tippable = appointments
            .Where(a => a.TipAmount == 0 && a.WalkInName == null && staffed.Contains(a.ResourceId) && CommissionService.IsFinished(a))
            .Select(a => a.Id).ToHashSet();
        return appointments.Select(a =>
        {
            var e = establishments[a.EstablishmentId];
            var late = a.Status != AppointmentStatus.RescheduleProposed && a.StartsAt - now < TimeSpan.FromMinutes(e.CancelLimitMinutes);
            var fee = 0m;
            if (late && AppointmentStateMachine.IsActive(a.Status) && e.LateCancelFeePercent > 0
                && paymentInfo.TryGetValue(a.Id, out var paid) && paid.Status == "Paid")
            {
                fee = KekeBeauty.Infrastructure.Billing.PaymentRules.LateFee(a.TotalPrice, e.LateCancelFeePercent, paid.Amount);
            }

            return new AppointmentDto(
                a.Id, a.EstablishmentId, e.Name, e.Slug, resources.GetValueOrDefault(a.ResourceId),
                a.StartsAt, a.EndsAt, a.Status.ToString(), a.TotalPrice, a.CurrencyCode, a.ClientNote, a.RejectionReason,
                a.ProposedStartsAt, a.ProposedEndsAt, a.ExpiresAt,
                AppointmentStateMachine.IsActive(a.Status) && (!late || fee > 0),
                lines.GetValueOrDefault(a.Id) ?? [],
                a.CreatedAt,
                paymentInfo.GetValueOrDefault(a.Id),
                AgendaService.ServicePaymentOf(a),
                a.TipAmount,
                tippable.Contains(a.Id),
                fee,
                a.Status is AppointmentStatus.Pending or AppointmentStatus.Confirmed && !a.AwaitingPayment && a.StartedAt == null
                    && !late && moves.GetValueOrDefault(a.Id) < MaxClientReschedules,
                a.Status == AppointmentStatus.Confirmed && a.StartedAt == null && !signalled.Contains(a.Id)
                    && now >= a.StartsAt.AddHours(-3) && now <= a.StartsAt.AddMinutes(30));
        }).ToList();
    }
}
