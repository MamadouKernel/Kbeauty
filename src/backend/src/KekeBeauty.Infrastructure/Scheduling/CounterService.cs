using KekeBeauty.Infrastructure.Partner;
using KekeBeauty.Infrastructure.Persistence;
using KekeBeauty.Modules.Scheduling.Domain;
using KekeBeauty.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace KekeBeauty.Infrastructure.Scheduling;

/// <summary>A client who walks in: the establishment records the appointment itself, the client has no account.</summary>
public sealed record CounterRequest(
    IReadOnlyList<Guid> ServiceIds,
    Guid? ResourceId,
    DateTimeOffset? StartsAt,
    string ClientName,
    string? Phone,
    string? PaidMethod,
    string? PaidReference);

/// <summary>The "comptoir": walk-in appointments recorded by the partner, confirmed at once and without lead-time rules.</summary>
public sealed class CounterService(
    KekeBeautyDbContext db,
    AvailabilityService availability,
    AppointmentWorkflow workflow,
    AgendaService agenda,
    TimeProvider time)
{
    public const int MinNameLength = 2;
    public const int MaxNameLength = 80;

    /// <summary>A walk-in starts now or within the same day: anything else is a normal booking.</summary>
    public static readonly TimeSpan MaxAdvance = TimeSpan.FromHours(12);

    public static readonly TimeSpan MaxPast = TimeSpan.FromMinutes(30);

    public async Task<Result<PartnerAppointmentDto>> CreateAsync(Guid userId, Guid establishmentId, CounterRequest request, CancellationToken ct)
    {
        if (!await db.Establishments.AnyAsync(e => e.Id == establishmentId && e.OwnerUserId == userId, ct))
        {
            return Result.Failure<PartnerAppointmentDto>(PartnerErrors.NotFound);
        }

        var name = request.ClientName?.Trim();
        if (name is not { Length: >= MinNameLength and <= MaxNameLength })
        {
            return Result.Failure<PartnerAppointmentDto>(SchedulingErrors.Invalid($"Le nom de la cliente doit compter de {MinNameLength} à {MaxNameLength} caractères."));
        }

        string? phone = null;
        if (!string.IsNullOrWhiteSpace(request.Phone))
        {
            phone = PartnerText.NormalizePhone(request.Phone);
            if (phone is null)
            {
                return Result.Failure<PartnerAppointmentDto>(SchedulingErrors.Invalid("Numéro de téléphone invalide."));
            }
        }

        var now = time.GetUtcNow();
        var start = request.StartsAt ?? new DateTimeOffset(now.Year, now.Month, now.Day, now.Hour, now.Minute, 0, TimeSpan.Zero);
        if (start < now - MaxPast || start > now + MaxAdvance)
        {
            return Result.Failure<PartnerAppointmentDto>(SchedulingErrors.Invalid("Le comptoir sert aux clientes présentes : l'heure doit être proche de maintenant."));
        }

        var loaded = await availability.LoadContextAsync(establishmentId, request.ServiceIds, request.ResourceId, ct);
        if (loaded.IsFailure)
        {
            return Result.Failure<PartnerAppointmentDto>(loaded.Error);
        }

        var context = loaded.Value;
        var end = start.AddMinutes(context.DurationMinutes);
        var free = await FreeResourcesAsync(context, start, end, ct);
        if (free.Count == 0)
        {
            return Result.Failure<PartnerAppointmentDto>(SchedulingErrors.SlotUnavailable);
        }

        Appointment? created = null;
        foreach (var resourceId in free)
        {
            var appointment = new Appointment
            {
                EstablishmentId = establishmentId,
                WalkInName = name,
                ResourceId = resourceId,
                StartsAt = start,
                EndsAt = end,
                Status = AppointmentStatus.Confirmed,
                TotalPrice = context.TotalPrice,
                CurrencyCode = context.Services[0].CurrencyCode,
                ClientPhone = phone,
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
                ToStatus = AppointmentStatus.Confirmed,
                ActorUserId = userId,
                At = now,
                Comment = "Comptoir",
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
            return Result.Failure<PartnerAppointmentDto>(SchedulingErrors.SlotTaken);
        }

        await workflow.AfterChangeAsync(created, null, null, ct);

        if (!string.IsNullOrWhiteSpace(request.PaidMethod))
        {
            return await agenda.MarkPaidAsync(userId, created.Id, new MarkPaidInput(request.PaidMethod, request.PaidReference), ct);
        }

        var list = await agenda.ListAsync(userId, establishmentId, null, created.StartsAt, created.StartsAt.AddMinutes(1), ct);
        return Result.Success(list.Value.First(a => a.Id == created.Id));
    }

    /// <summary>Resources free for the exact interval, whatever the slot grid: a walk-in does not wait for the next slot.</summary>
    private async Task<IReadOnlyList<Guid>> FreeResourcesAsync(BookingContext context, DateTimeOffset start, DateTimeOffset end, CancellationToken ct)
    {
        var candidateIds = context.Candidates.Select(r => r.Id).ToList();
        var busy = await db.Appointments.AsNoTracking()
            .Where(a => a.EstablishmentId == context.Establishment.Id
                && candidateIds.Contains(a.ResourceId)
                && AppointmentStateMachine.ActiveStatuses.Contains(a.Status)
                && a.StartsAt < end && a.EndsAt > start)
            .Select(a => a.ResourceId)
            .ToListAsync(ct);
        var closed = await db.TimeOffs.AsNoTracking()
            .Where(t => t.EstablishmentId == context.Establishment.Id && t.StartsAt < end && t.EndsAt > start)
            .Select(t => t.ResourceId)
            .ToListAsync(ct);
        if (closed.Contains(null))
        {
            return [];
        }

        return candidateIds.Where(id => !busy.Contains(id) && !closed.Contains(id)).ToList();
    }
}
