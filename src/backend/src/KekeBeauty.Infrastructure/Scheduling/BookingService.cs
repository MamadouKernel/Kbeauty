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
            .ToDictionary(g => g.Key, g => (IReadOnlyList<AppointmentLineDto>)g.Select(l => new AppointmentLineDto(l.NameSnapshot, l.PriceSnapshot, l.DurationSnapshot)).ToList());

        var now = time.GetUtcNow();
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
                fee);
        }).ToList();
    }
}
