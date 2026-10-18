using KekeBeauty.Infrastructure.Partner;
using KekeBeauty.Infrastructure.Persistence;
using KekeBeauty.Modules.Scheduling.Domain;
using KekeBeauty.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace KekeBeauty.Infrastructure.Scheduling;

/// <summary>
/// Commissions and tips of the team. A finished service earns its collaborator a share of the price (the resource's rate);
/// the tip is the client's own gift and is added on top. The platform only computes and records: the establishment pays its staff.
/// </summary>
public sealed class CommissionService(KekeBeautyDbContext db, TimeProvider time)
{
    public const decimal MinTip = 100m;
    public const decimal MaxTip = 100_000m;
    public const int MaxPeriodDays = 366;
    public const int DefaultPeriodDays = 30;

    public static readonly Error NotTippable = new("tip.not_tippable", "Un pourboire se laisse après un soin terminé, pour une collaboratrice identifiée.");
    public static readonly Error AlreadyTipped = new("tip.already_tipped", "Vous avez déjà laissé un pourboire pour ce rendez-vous.");
    private static readonly Error TeamNotFound = new("team.not_found", "Collaboratrice introuvable.");

    /// <summary>What a collaborator earned on every resource she serves.</summary>
    public async Task<Result<IReadOnlyList<CommissionSummaryDto>>> ForCollaboratorAsync(Guid userId, DateOnly? from, DateOnly? to, CancellationToken ct)
    {
        var resources = await db.Resources.AsNoTracking().Where(r => r.UserId == userId).ToListAsync(ct);
        if (resources.Count == 0)
        {
            return Result.Failure<IReadOnlyList<CommissionSummaryDto>>(TeamNotFound);
        }

        return await SummariseAsync(resources, from, to, ct);
    }

    /// <summary>What every resource of an establishment earned, for the owner's accounting.</summary>
    public async Task<Result<IReadOnlyList<CommissionSummaryDto>>> ForOwnerAsync(Guid ownerId, Guid establishmentId, DateOnly? from, DateOnly? to, CancellationToken ct)
    {
        if (!await PartnerSupport.OwnedBy(db, ownerId, establishmentId).AnyAsync(ct))
        {
            return Result.Failure<IReadOnlyList<CommissionSummaryDto>>(PartnerErrors.NotFound);
        }

        var resources = await db.Resources.AsNoTracking().Where(r => r.EstablishmentId == establishmentId).ToListAsync(ct);
        return await SummariseAsync(resources, from, to, ct);
    }

    /// <summary>The client thanks the person who served her. One tip per appointment, only once the treatment is over.</summary>
    /// <summary>Validates a tip request and returns the appointment it is for; nothing is written.</summary>
    public async Task<Result<Appointment>> CheckTipAsync(Guid clientId, Guid appointmentId, decimal amount, CancellationToken ct)
    {
        var appointment = await db.Appointments.FirstOrDefaultAsync(a => a.Id == appointmentId && a.ClientUserId == clientId, ct);
        if (appointment is null)
        {
            return Result.Failure<Appointment>(SchedulingErrors.AppointmentNotFound);
        }

        if (amount < MinTip || amount > MaxTip || amount != decimal.Truncate(amount))
        {
            return Result.Failure<Appointment>(SchedulingErrors.Invalid($"Le pourboire doit être un montant entier compris entre {MinTip:0} et {MaxTip:0}."));
        }

        if (!await IsTippableAsync(appointment, ct))
        {
            return Result.Failure<Appointment>(NotTippable);
        }

        return appointment.TipAmount > 0 ? Result.Failure<Appointment>(AlreadyTipped) : Result.Success(appointment);
    }

    /// <summary>Records a tip handed over directly (no online payment on the platform). One per appointment.</summary>
    public async Task<Result<decimal>> TipAsync(Guid clientId, Guid appointmentId, decimal amount, CancellationToken ct)
    {
        var allowed = await CheckTipAsync(clientId, appointmentId, amount, ct);
        if (allowed.IsFailure)
        {
            return Result.Failure<decimal>(allowed.Error);
        }

        // Conditional write: two simultaneous requests cannot both succeed.
        var changed = await db.Appointments.Where(a => a.Id == appointmentId && a.TipAmount == 0)
            .ExecuteUpdateAsync(set => set.SetProperty(a => a.TipAmount, amount), ct);
        return changed == 0 ? Result.Failure<decimal>(AlreadyTipped) : Result.Success(amount);
    }

    /// <summary>True when the client may still leave a tip on this appointment.</summary>
    public async Task<bool> CanTipAsync(Appointment appointment, CancellationToken ct) =>
        appointment.TipAmount == 0 && await IsTippableAsync(appointment, ct);

    public static bool IsFinished(Appointment a) =>
        a.Status == AppointmentStatus.Completed || (a.Status == AppointmentStatus.Confirmed && a.FinishedAt != null);

    /// <summary>Freezes what the collaborator earned on a service that is done (once), so that later changes never rewrite history.</summary>
    public static void Snapshot(Appointment appointment, Resource resource)
    {
        if (appointment.CommissionAmount is not null)
        {
            return;
        }

        appointment.CommissionAmount = CommissionOf(appointment.TotalPrice, resource.CommissionPercent);
        appointment.CommissionUserId = resource.UserId;
    }

    public static decimal CommissionOf(decimal price, decimal percent) => decimal.Round(price * percent / 100m, 0, MidpointRounding.AwayFromZero);

    private Task<bool> IsTippableAsync(Appointment a, CancellationToken ct) =>
        !IsFinished(a) || a.WalkInName != null
            ? Task.FromResult(false)
            : db.Resources.AnyAsync(r => r.Id == a.ResourceId && r.UserId != null, ct);

    private async Task<Result<IReadOnlyList<CommissionSummaryDto>>> SummariseAsync(IReadOnlyList<Resource> resources, DateOnly? from, DateOnly? to, CancellationToken ct)
    {
        var today = DateOnly.FromDateTime(time.GetUtcNow().UtcDateTime);
        var end = to ?? today;
        var start = from ?? end.AddDays(1 - DefaultPeriodDays);
        if (start > end || end.DayNumber - start.DayNumber >= MaxPeriodDays)
        {
            return Result.Failure<IReadOnlyList<CommissionSummaryDto>>(SchedulingErrors.Invalid($"Période invalide : au plus {MaxPeriodDays} jours."));
        }

        var since = new DateTimeOffset(start.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero);
        var until = new DateTimeOffset(end.AddDays(1).ToDateTime(TimeOnly.MinValue), TimeSpan.Zero);
        var resourceIds = resources.Select(r => r.Id).ToList();

        var done = await db.Appointments.AsNoTracking()
            .Where(a => resourceIds.Contains(a.ResourceId) && !a.AwaitingPayment && a.StartsAt >= since && a.StartsAt < until
                && (a.Status == AppointmentStatus.Completed || (a.Status == AppointmentStatus.Confirmed && a.FinishedAt != null)))
            .OrderBy(a => a.StartsAt).ToListAsync(ct);
        var clientIds = done.Where(a => a.ClientUserId != null).Select(a => a.ClientUserId!.Value).Distinct().ToList();
        var names = await db.Users.AsNoTracking().Where(u => clientIds.Contains(u.Id))
            .ToDictionaryAsync(u => u.Id, u => $"{u.FirstName} {u.LastName}".Trim(), ct);

        var summaries = resources.Select(resource =>
        {
            var lines = done.Where(a => a.ResourceId == resource.Id).Select(a => new CommissionLineDto(
                a.Id,
                a.FinishedAt ?? a.EndsAt,
                a.WalkInName ?? (a.ClientUserId is { } id && names.TryGetValue(id, out var name) ? name : "Cliente"),
                a.TotalPrice,
                a.CommissionAmount ?? CommissionOf(a.TotalPrice, resource.CommissionPercent),
                a.TipAmount)).ToList();
            var revenue = lines.Sum(l => l.ServicePrice);
            var commission = lines.Sum(l => l.Commission);
            var tips = lines.Sum(l => l.Tip);
            return new CommissionSummaryDto(resource.Id, resource.Name, resource.CommissionPercent, lines.Count, revenue, commission, tips, commission + tips, "XOF", lines);
        }).ToList();
        return Result.Success<IReadOnlyList<CommissionSummaryDto>>(summaries);
    }
}
