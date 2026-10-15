namespace KekeBeauty.Modules.Scheduling.Domain;

public sealed record BookingRules(int StepMinutes, int MinLeadMinutes, int MaxHorizonDays);

public sealed record OpenRange(TimeOnly Opens, TimeOnly Closes);

/// <summary>A period during which a resource (or, when <see cref="ResourceId"/> is null, everybody) is unavailable.</summary>
public sealed record BusyPeriod(Guid? ResourceId, DateTimeOffset Start, DateTimeOffset End);

public sealed record Slot(DateTimeOffset Start, DateTimeOffset End, IReadOnlyList<Guid> FreeResources)
{
    public bool Available => FreeResources.Count > 0;
}

public sealed record DaySlots(DateOnly Date, bool IsOpen, IReadOnlyList<Slot> Slots);

/// <summary>
/// Pure slot computation (PRD RDV-02): opening hours, minus closures and existing appointments, for a total duration,
/// on a fixed grid, honouring the minimum lead time and the booking horizon. A slot is free when at least one of the
/// candidate resources is free during the whole duration.
/// </summary>
public static class SlotCalculator
{
    public static IReadOnlyList<DaySlots> Compute(
        DateOnly from,
        DateOnly to,
        int durationMinutes,
        IReadOnlyDictionary<DayOfWeek, IReadOnlyList<OpenRange>> hours,
        IReadOnlyList<Guid> candidateResources,
        IReadOnlyList<BusyPeriod> busy,
        BookingRules rules,
        TimeZoneInfo zone,
        DateTimeOffset now)
    {
        if (durationMinutes <= 0 || rules.StepMinutes <= 0)
        {
            return [];
        }

        var earliest = now.AddMinutes(rules.MinLeadMinutes);
        var latest = now.AddDays(rules.MaxHorizonDays);
        var days = new List<DaySlots>();

        for (var day = from; day <= to; day = day.AddDays(1))
        {
            var ranges = hours.TryGetValue(day.DayOfWeek, out var configured) ? configured : [];
            var slots = new List<Slot>();

            foreach (var range in ranges.Where(r => r.Closes > r.Opens).OrderBy(r => r.Opens))
            {
                var opens = range.Opens.Hour * 60 + range.Opens.Minute;
                var closes = range.Closes.Hour * 60 + range.Closes.Minute;

                for (var minute = opens; minute + durationMinutes <= closes; minute += rules.StepMinutes)
                {
                    if (!TryToUtc(day, minute, zone, out var start))
                    {
                        continue;
                    }

                    var end = start.AddMinutes(durationMinutes);
                    var free = start < earliest || start > latest
                        ? []
                        : candidateResources.Where(resource => !busy.Any(b => Overlaps(b, resource, start, end))).ToList();
                    slots.Add(new Slot(start, end, free));
                }
            }

            days.Add(new DaySlots(day, ranges.Count > 0, slots));
        }

        return days;
    }

    private static bool Overlaps(BusyPeriod period, Guid resource, DateTimeOffset start, DateTimeOffset end) =>
        (period.ResourceId is null || period.ResourceId == resource) && period.Start < end && period.End > start;

    /// <summary>Local wall-clock time of an establishment to UTC; times skipped by a DST gap are not bookable.</summary>
    private static bool TryToUtc(DateOnly day, int minuteOfDay, TimeZoneInfo zone, out DateTimeOffset utc)
    {
        var local = day.ToDateTime(TimeOnly.MinValue, DateTimeKind.Unspecified).AddMinutes(minuteOfDay);
        if (zone.IsInvalidTime(local))
        {
            utc = default;
            return false;
        }

        utc = new DateTimeOffset(TimeZoneInfo.ConvertTimeToUtc(local, zone), TimeSpan.Zero);
        return true;
    }
}
