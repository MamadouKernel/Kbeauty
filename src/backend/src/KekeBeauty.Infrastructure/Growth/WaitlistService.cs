using System.Globalization;
using KekeBeauty.Infrastructure.Partner;
using KekeBeauty.Infrastructure.Persistence;
using KekeBeauty.Infrastructure.Scheduling;
using KekeBeauty.Modules.Catalog.Domain;
using KekeBeauty.Modules.Scheduling.Domain;
using KekeBeauty.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace KekeBeauty.Infrastructure.Growth;

public sealed record WaitlistEntryDto(
    Guid Id,
    Guid EstablishmentId,
    string EstablishmentName,
    string EstablishmentSlug,
    DateOnly Date,
    string? ServiceName,
    string Status,
    string? ClientName,
    DateTimeOffset CreatedAt,
    DateTimeOffset? NotifiedAt);

public sealed record JoinWaitlistInput(Guid EstablishmentId, DateOnly Date, Guid? ServiceId);

/// <summary>
/// Automatic waiting list: a client who found the day full is told as soon as an appointment of that day is cancelled,
/// refused or expires. Everybody waiting for the day is told at once and the entry is spent; the slot goes to whoever books first.
/// </summary>
public sealed class WaitlistService(KekeBeautyDbContext db, NotificationService notifications, TimeProvider time)
{
    /// <summary>Live entries a client may hold at once, all establishments together.</summary>
    public const int MaxLiveEntriesPerClient = 10;

    /// <summary>People told about one freed slot: beyond that the slot is long gone anyway.</summary>
    public const int MaxNotifiedPerSlot = 50;

    private static readonly Error NotFound = new("waitlist.not_found", "Inscription introuvable.");
    private static readonly Error TooMany = new("waitlist.too_many", $"Vous attendez déjà un créneau sur {MaxLiveEntriesPerClient} journées : retirez-en une avant d'en ajouter.");
    private static readonly Error BadDate = new("waitlist.bad_date", "Choisissez une date à venir, dans la période où l'établissement prend des rendez-vous.");

    private static readonly AppointmentStatus[] Freeing =
        [AppointmentStatus.CancelledByClient, AppointmentStatus.CancelledByEstablishment, AppointmentStatus.Rejected, AppointmentStatus.Expired];

    // ───────── Client ─────────

    public async Task<Result<WaitlistEntryDto>> JoinAsync(Guid clientId, JoinWaitlistInput input, CancellationToken ct)
    {
        var establishment = await db.Establishments.AsNoTracking()
            .Where(e => e.Id == input.EstablishmentId && e.Status == EstablishmentStatus.Active)
            .Select(e => new { e.Id, e.Timezone, e.MaxHorizonDays, Bookable = e.BookingEnabled && e.BookingAllowed })
            .FirstOrDefaultAsync(ct);
        if (establishment is null)
        {
            return Result.Failure<WaitlistEntryDto>(SchedulingErrors.EstablishmentNotFound);
        }

        if (!establishment.Bookable)
        {
            return Result.Failure<WaitlistEntryDto>(SchedulingErrors.BookingClosed);
        }

        var today = DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(time.GetUtcNow(), AppointmentWorkflow.ZoneOf(establishment.Timezone)).DateTime);
        if (input.Date < today || input.Date > today.AddDays(establishment.MaxHorizonDays))
        {
            return Result.Failure<WaitlistEntryDto>(BadDate);
        }

        if (input.ServiceId is { } serviceId
            && !await db.Services.AnyAsync(s => s.Id == serviceId && s.EstablishmentId == establishment.Id && s.DeletedAt == null, ct))
        {
            return Result.Failure<WaitlistEntryDto>(SchedulingErrors.Invalid("Prestation inconnue pour cet établissement."));
        }

        // Asking twice for the same day is not an error: the client simply stays on the list.
        var existing = await db.WaitlistEntries.AsNoTracking().FirstOrDefaultAsync(w => w.EstablishmentId == establishment.Id
            && w.ClientUserId == clientId && w.Date == input.Date && w.Status == WaitlistStatus.Waiting, ct);
        if (existing is not null)
        {
            return Result.Success((await ProjectAsync([existing], withClient: false, ct))[0]);
        }

        if (await LiveFor(clientId, today).CountAsync(ct) >= MaxLiveEntriesPerClient)
        {
            return Result.Failure<WaitlistEntryDto>(TooMany);
        }

        var entry = new WaitlistEntry
        {
            EstablishmentId = establishment.Id,
            ClientUserId = clientId,
            Date = input.Date,
            ServiceId = input.ServiceId,
            CreatedAt = time.GetUtcNow(),
        };
        db.WaitlistEntries.Add(entry);
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException)
        {
            // Two clicks at once: the filtered unique index keeps one entry, which is what the client wanted.
            db.ChangeTracker.Clear();
            var kept = await db.WaitlistEntries.AsNoTracking().FirstAsync(w => w.EstablishmentId == establishment.Id
                && w.ClientUserId == clientId && w.Date == input.Date && w.Status == WaitlistStatus.Waiting, ct);
            return Result.Success((await ProjectAsync([kept], withClient: false, ct))[0]);
        }

        return Result.Success((await ProjectAsync([entry], withClient: false, ct))[0]);
    }

    public async Task<IReadOnlyList<WaitlistEntryDto>> MineAsync(Guid clientId, CancellationToken ct) =>
        await ProjectAsync(
            await db.WaitlistEntries.AsNoTracking().Where(w => w.ClientUserId == clientId && w.Status != WaitlistStatus.Cancelled)
                .OrderByDescending(w => w.Date).ThenByDescending(w => w.CreatedAt).Take(50).ToListAsync(ct),
            withClient: false,
            ct);

    public async Task<Result> LeaveAsync(Guid clientId, Guid entryId, CancellationToken ct)
    {
        var left = await db.WaitlistEntries
            .Where(w => w.Id == entryId && w.ClientUserId == clientId && w.Status == WaitlistStatus.Waiting)
            .ExecuteUpdateAsync(set => set.SetProperty(w => w.Status, WaitlistStatus.Cancelled), ct);
        return left == 1 ? Result.Success() : Result.Failure(NotFound);
    }

    // ───────── Establishment ─────────

    /// <summary>Who waits for which upcoming day, so the establishment can call them itself too.</summary>
    public async Task<Result<IReadOnlyList<WaitlistEntryDto>>> ForEstablishmentAsync(Guid ownerId, Guid establishmentId, CancellationToken ct)
    {
        var establishment = await PartnerSupport.OwnedBy(db, ownerId, establishmentId).Select(e => new { e.Timezone }).FirstOrDefaultAsync(ct);
        if (establishment is null)
        {
            return Result.Failure<IReadOnlyList<WaitlistEntryDto>>(PartnerErrors.NotFound);
        }

        var today = DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(time.GetUtcNow(), AppointmentWorkflow.ZoneOf(establishment.Timezone)).DateTime);
        var entries = await db.WaitlistEntries.AsNoTracking()
            .Where(w => w.EstablishmentId == establishmentId && w.Date >= today && w.Status != WaitlistStatus.Cancelled)
            .OrderBy(w => w.Date).ThenBy(w => w.CreatedAt).Take(200).ToListAsync(ct);
        return Result.Success(await ProjectAsync(entries, withClient: true, ct));
    }

    // ───────── Trigger ─────────

    /// <summary>
    /// Called after every saved status change: when a future appointment that held a slot stops holding it, everyone
    /// waiting for that day is told. Returns how many people were told.
    /// </summary>
    public async Task<int> AfterChangeAsync(Appointment appointment, AppointmentStatus? from, CancellationToken ct)
    {
        var now = time.GetUtcNow();
        if (from is not (AppointmentStatus.Pending or AppointmentStatus.Confirmed or AppointmentStatus.RescheduleProposed)
            || !Freeing.Contains(appointment.Status) || appointment.StartsAt <= now)
        {
            return 0;
        }

        var establishment = await db.Establishments.AsNoTracking()
            .Where(e => e.Id == appointment.EstablishmentId)
            .Select(e => new { e.Name, e.Timezone, e.Slug })
            .FirstAsync(ct);
        var local = TimeZoneInfo.ConvertTime(appointment.StartsAt, AppointmentWorkflow.ZoneOf(establishment.Timezone));
        var day = DateOnly.FromDateTime(local.DateTime);
        var waiting = await db.WaitlistEntries.AsNoTracking()
            .Where(w => w.EstablishmentId == appointment.EstablishmentId && w.Date == day && w.Status == WaitlistStatus.Waiting
                && w.ClientUserId != appointment.ClientUserId)
            .OrderBy(w => w.CreatedAt)
            .Take(MaxNotifiedPerSlot)
            .Select(w => new { w.Id, w.ClientUserId })
            .ToListAsync(ct);

        var told = 0;
        var date = local.ToString("dd/MM/yyyy", CultureInfo.InvariantCulture);
        var hour = local.ToString("HH:mm", CultureInfo.InvariantCulture);
        foreach (var entry in waiting)
        {
            // Claim the entry first: a second cancellation of the same day running at the same time tells nobody twice.
            var claimed = await db.WaitlistEntries
                .Where(w => w.Id == entry.Id && w.Status == WaitlistStatus.Waiting)
                .ExecuteUpdateAsync(set => set.SetProperty(w => w.Status, WaitlistStatus.Notified).SetProperty(w => w.NotifiedAt, now), ct);
            if (claimed == 0)
            {
                continue;
            }

            var variables = new Dictionary<string, string>
            {
                ["etablissement"] = establishment.Name,
                ["client"] = string.Empty,
                ["date"] = date,
                ["heure"] = hour,
                ["motif"] = string.Empty,
            };
            await notifications.NotifyAsync(entry.ClientUserId, "waitlist.slot_freed", "Un créneau s'est libéré",
                $"Un créneau vient de se libérer chez {establishment.Name} le {date} à {hour}. Réservez vite : le premier qui réserve l'obtient.",
                null, ct, variables);
            told++;
        }

        return told;
    }

    // ───────── Projection ─────────

    private IQueryable<WaitlistEntry> LiveFor(Guid clientId, DateOnly today) =>
        db.WaitlistEntries.Where(w => w.ClientUserId == clientId && w.Status == WaitlistStatus.Waiting && w.Date >= today);

    private async Task<IReadOnlyList<WaitlistEntryDto>> ProjectAsync(IReadOnlyList<WaitlistEntry> entries, bool withClient, CancellationToken ct)
    {
        if (entries.Count == 0)
        {
            return [];
        }

        var establishmentIds = entries.Select(w => w.EstablishmentId).Distinct().ToList();
        var serviceIds = entries.Where(w => w.ServiceId != null).Select(w => w.ServiceId!.Value).Distinct().ToList();
        var establishments = await db.Establishments.AsNoTracking().Where(e => establishmentIds.Contains(e.Id))
            .ToDictionaryAsync(e => e.Id, e => new { e.Name, e.Slug, e.Timezone }, ct);
        var services = await db.Services.AsNoTracking().Where(s => serviceIds.Contains(s.Id)).ToDictionaryAsync(s => s.Id, s => s.Name, ct);
        var clients = new Dictionary<Guid, string>();
        if (withClient)
        {
            var clientIds = entries.Select(w => w.ClientUserId).Distinct().ToList();
            clients = await db.Users.AsNoTracking().Where(u => clientIds.Contains(u.Id))
                .ToDictionaryAsync(u => u.Id, u => $"{u.FirstName} {u.LastName}".Trim(), ct);
        }

        var now = time.GetUtcNow();
        return entries.Select(w =>
        {
            var e = establishments[w.EstablishmentId];
            var today = DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(now, AppointmentWorkflow.ZoneOf(e.Timezone)).DateTime);

            // A day gone by without any freed slot: shown as expired, nothing to store.
            var status = w.Status == WaitlistStatus.Waiting && w.Date < today ? "Expired" : w.Status.ToString();
            return new WaitlistEntryDto(
                w.Id, w.EstablishmentId, e.Name, e.Slug, w.Date,
                w.ServiceId is { } serviceId ? services.GetValueOrDefault(serviceId) : null,
                status,
                withClient ? clients.GetValueOrDefault(w.ClientUserId) ?? "Cliente" : null,
                w.CreatedAt, w.NotifiedAt);
        }).ToList();
    }
}
