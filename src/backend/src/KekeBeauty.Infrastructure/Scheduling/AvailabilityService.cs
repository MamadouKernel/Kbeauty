using System.Globalization;
using KekeBeauty.Infrastructure.Persistence;
using KekeBeauty.Modules.Catalog.Domain;
using KekeBeauty.Modules.Scheduling.Domain;
using KekeBeauty.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace KekeBeauty.Infrastructure.Scheduling;

/// <summary>Everything needed to answer "is this slot free for these services?" for one establishment.</summary>
public sealed record BookingContext(
    Establishment Establishment,
    IReadOnlyList<Service> Services,
    int DurationMinutes,
    decimal TotalPrice,
    IReadOnlyList<Resource> Candidates,
    TimeZoneInfo Zone,
    IReadOnlyDictionary<DayOfWeek, IReadOnlyList<OpenRange>> Hours)
{
    public BookingRules Rules => new(Establishment.SlotStepMinutes, Establishment.MinLeadMinutes, Establishment.MaxHorizonDays);
}

/// <summary>Public booking information and the slot calendar (PRD RDV-01, RDV-02).</summary>
public sealed class AvailabilityService(KekeBeautyDbContext db, TimeProvider time, KekeBeauty.Infrastructure.Admin.AppSettingsService settings)
{
    public const int MaxServicesPerBooking = 6;
    public const int MaxRangeDays = 62;

    public async Task<BookingInfo?> GetInfoAsync(string slug, CancellationToken ct)
    {
        var e = await db.Establishments.AsNoTracking()
            .FirstOrDefaultAsync(x => x.Slug == slug && x.Status == EstablishmentStatus.Active && x.BookingEnabled && x.BookingAllowed, ct);
        if (e is null)
        {
            return null;
        }

        var services = await db.Services.AsNoTracking()
            .Where(s => s.EstablishmentId == e.Id && s.IsBookable && s.IsVisible && s.DeletedAt == null)
            .OrderBy(s => s.SortOrder)
            .Select(s => new BookableService(s.Id, s.Name, s.Description, s.Price, s.PriceType.ToString(), s.CurrencyCode, s.DurationMinutes))
            .ToListAsync(ct);
        var resources = await db.Resources.AsNoTracking()
            .Where(r => r.EstablishmentId == e.Id && r.IsActive)
            .OrderBy(r => r.CreatedAt).ThenBy(r => r.Id)
            .ToListAsync(ct);
        var links = await LinksAsync(resources.Select(r => r.Id).ToList(), ct);

        return new BookingInfo(
            e.Id, e.Name, e.Slug, e.Timezone, e.MaxHorizonDays, e.MinLeadMinutes, services,
            resources.Select(r => new ResourceDto(r.Id, r.Name, r.IsActive, links.GetValueOrDefault(r.Id) ?? [])).ToList());
    }

    public async Task<Result<AvailabilityResponse>> GetAvailabilityAsync(
        Guid establishmentId,
        DateOnly from,
        DateOnly to,
        IReadOnlyList<Guid> serviceIds,
        Guid? resourceId,
        CancellationToken ct)
    {
        if (to < from || to.DayNumber - from.DayNumber > MaxRangeDays)
        {
            return Result.Failure<AvailabilityResponse>(SchedulingErrors.Invalid($"La période doit couvrir entre 1 et {MaxRangeDays} jours."));
        }

        var loaded = await LoadContextAsync(establishmentId, serviceIds, resourceId, ct);
        if (loaded.IsFailure)
        {
            return Result.Failure<AvailabilityResponse>(loaded.Error);
        }

        var context = loaded.Value;
        var days = await ComputeAsync(context, from, to, excludeAppointmentId: null, ct);
        var platform = await settings.GetAsync(ct);
        var plan = KekeBeauty.Infrastructure.Billing.PaymentRules.Plan(context.Establishment, context.TotalPrice, context.Services[0].CurrencyCode, platform.AppointmentPaymentsEnabled, platform.PlatformFeePercent);
        var response = new AvailabilityResponse(
            context.Establishment.Timezone,
            context.DurationMinutes,
            context.TotalPrice,
            context.Services[0].CurrencyCode,
            days.Select(d => new AvailabilityDay(
                    d.Date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                    d.IsOpen,
                    d.Slots.Any(s => s.Available),
                    d.Slots.Select(s => new AvailabilitySlot(
                            s.Start,
                            TimeZoneInfo.ConvertTime(s.Start, context.Zone).ToString("HH:mm", CultureInfo.InvariantCulture),
                            s.Available))
                        .ToList()))
                .ToList(),
            plan is null ? null : KekeBeauty.Infrastructure.Billing.PaymentRules.Quote(plan, context.TotalPrice));
        return Result.Success(response);
    }

    public async Task<Result<BookingContext>> LoadContextAsync(
        Guid establishmentId,
        IReadOnlyList<Guid> serviceIds,
        Guid? resourceId,
        CancellationToken ct)
    {
        var e = await db.Establishments.AsNoTracking().FirstOrDefaultAsync(x => x.Id == establishmentId && x.Status == EstablishmentStatus.Active, ct);
        if (e is null)
        {
            return Result.Failure<BookingContext>(SchedulingErrors.EstablishmentNotFound);
        }

        if (!e.BookingEnabled || !e.BookingAllowed)
        {
            return Result.Failure<BookingContext>(SchedulingErrors.BookingClosed);
        }

        var ids = serviceIds?.Distinct().ToList() ?? [];
        if (ids.Count is < 1 or > MaxServicesPerBooking)
        {
            return Result.Failure<BookingContext>(SchedulingErrors.Invalid($"Choisissez entre 1 et {MaxServicesPerBooking} prestations."));
        }

        var services = await db.Services.AsNoTracking()
            .Where(s => s.EstablishmentId == e.Id && ids.Contains(s.Id) && s.IsBookable && s.IsVisible && s.DeletedAt == null)
            .ToListAsync(ct);
        if (services.Count != ids.Count)
        {
            return Result.Failure<BookingContext>(SchedulingErrors.Invalid("Prestation inconnue ou non réservable en ligne."));
        }

        var resources = await db.Resources.AsNoTracking()
            .Where(r => r.EstablishmentId == e.Id && r.IsActive)
            .OrderBy(r => r.CreatedAt).ThenBy(r => r.Id)
            .ToListAsync(ct);
        var links = await LinksAsync(resources.Select(r => r.Id).ToList(), ct);
        var capable = resources
            .Where(r => !links.TryGetValue(r.Id, out var skills) || ids.All(skills.Contains))
            .ToList();

        var candidates = capable;
        if (resourceId is { } wanted)
        {
            candidates = capable.Where(r => r.Id == wanted).ToList();
            if (candidates.Count == 0)
            {
                return Result.Failure<BookingContext>(SchedulingErrors.Invalid("Ce professionnel ne propose pas toutes les prestations choisies."));
            }
        }

        if (candidates.Count == 0)
        {
            return Result.Failure<BookingContext>(SchedulingErrors.BookingClosed);
        }

        var hours = (await db.OpeningHours.AsNoTracking().Where(h => h.EstablishmentId == e.Id).ToListAsync(ct))
            .GroupBy(h => h.DayOfWeek)
            .ToDictionary(g => g.Key, g => (IReadOnlyList<OpenRange>)g.Select(h => new OpenRange(h.OpensAt, h.ClosesAt)).ToList());

        return Result.Success(new BookingContext(
            e, services, services.Sum(s => s.DurationMinutes), services.Sum(s => s.Price), candidates,
            AppointmentWorkflow.ZoneOf(e.Timezone), hours));
    }

    /// <summary>Resources free for the exact slot starting at <paramref name="start"/>; empty when the slot is not bookable.</summary>
    public async Task<IReadOnlyList<Guid>> FreeResourcesAtAsync(BookingContext context, DateTimeOffset start, Guid? excludeAppointmentId, CancellationToken ct)
    {
        var day = DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(start, context.Zone).DateTime);
        var days = await ComputeAsync(context, day, day, excludeAppointmentId, ct);
        return days.SelectMany(d => d.Slots).FirstOrDefault(s => s.Start == start)?.FreeResources ?? [];
    }

    private async Task<IReadOnlyList<DaySlots>> ComputeAsync(BookingContext context, DateOnly from, DateOnly to, Guid? excludeAppointmentId, CancellationToken ct)
    {
        var windowStart = new DateTimeOffset(from.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero).AddDays(-1);
        var windowEnd = new DateTimeOffset(to.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero).AddDays(2);
        var resourceIds = context.Candidates.Select(r => r.Id).ToList();

        var appointments = await db.Appointments.AsNoTracking()
            .Where(a => a.EstablishmentId == context.Establishment.Id
                && resourceIds.Contains(a.ResourceId)
                && AppointmentStateMachine.ActiveStatuses.Contains(a.Status)
                && a.StartsAt < windowEnd && a.EndsAt > windowStart
                && (excludeAppointmentId == null || a.Id != excludeAppointmentId))
            .Select(a => new BusyPeriod(a.ResourceId, a.StartsAt, a.EndsAt))
            .ToListAsync(ct);
        var closures = await db.TimeOffs.AsNoTracking()
            .Where(t => t.EstablishmentId == context.Establishment.Id
                && (t.ResourceId == null || resourceIds.Contains(t.ResourceId.Value))
                && t.StartsAt < windowEnd && t.EndsAt > windowStart)
            .Select(t => new BusyPeriod(t.ResourceId, t.StartsAt, t.EndsAt))
            .ToListAsync(ct);

        return SlotCalculator.Compute(
            from, to, context.DurationMinutes, context.Hours, resourceIds, [.. appointments, .. closures],
            context.Rules, context.Zone, time.GetUtcNow());
    }

    private async Task<Dictionary<Guid, List<Guid>>> LinksAsync(List<Guid> resourceIds, CancellationToken ct) =>
        (await db.ResourceServiceLinks.AsNoTracking().Where(l => resourceIds.Contains(l.ResourceId)).ToListAsync(ct))
        .GroupBy(l => l.ResourceId)
        .ToDictionary(g => g.Key, g => g.Select(l => l.ServiceId).ToList());
}
