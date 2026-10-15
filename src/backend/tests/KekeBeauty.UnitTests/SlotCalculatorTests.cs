using FluentAssertions;
using KekeBeauty.Modules.Scheduling.Domain;

namespace KekeBeauty.UnitTests;

public sealed class SlotCalculatorTests
{
    private static readonly DateOnly Monday = new(2026, 10, 12);
    private static readonly Guid Anna = Guid.Parse("00000000-0000-0000-0000-00000000000a");
    private static readonly Guid Bella = Guid.Parse("00000000-0000-0000-0000-00000000000b");

    // Far enough before the Monday so that neither lead time nor horizon get in the way by default.
    private static readonly DateTimeOffset LongBefore = new(2026, 10, 1, 8, 0, 0, TimeSpan.Zero);

    private static IReadOnlyDictionary<DayOfWeek, IReadOnlyList<OpenRange>> Hours(params (DayOfWeek Day, string Opens, string Closes)[] ranges) =>
        ranges.GroupBy(r => r.Day).ToDictionary(
            g => g.Key,
            g => (IReadOnlyList<OpenRange>)g.Select(r => new OpenRange(TimeOnly.Parse(r.Opens), TimeOnly.Parse(r.Closes))).ToList());

    private static DaySlots Day(
        int durationMinutes = 60,
        string opens = "09:00",
        string closes = "12:00",
        Guid[]? resources = null,
        BusyPeriod[]? busy = null,
        BookingRules? rules = null,
        DateTimeOffset? now = null,
        TimeZoneInfo? zone = null,
        IReadOnlyDictionary<DayOfWeek, IReadOnlyList<OpenRange>>? hours = null) =>
        SlotCalculator.Compute(
            Monday,
            Monday,
            durationMinutes,
            hours ?? Hours((DayOfWeek.Monday, opens, closes)),
            resources ?? [Anna],
            busy ?? [],
            rules ?? new BookingRules(StepMinutes: 30, MinLeadMinutes: 0, MaxHorizonDays: 60),
            zone ?? TimeZoneInfo.Utc,
            now ?? LongBefore).Single();

    private static DateTimeOffset At(int hour, int minute = 0) => new(2026, 10, 12, hour, minute, 0, TimeSpan.Zero);

    [Fact]
    public void Slots_follow_the_grid_and_stop_when_the_service_no_longer_fits()
    {
        var day = Day(durationMinutes: 60);

        day.IsOpen.Should().BeTrue();
        day.Slots.Select(s => s.Start).Should().Equal(At(9), At(9, 30), At(10), At(10, 30), At(11));
        day.Slots.Should().OnlyContain(s => s.Available && s.End - s.Start == TimeSpan.FromHours(1));
    }

    [Fact]
    public void A_longer_total_duration_removes_the_last_slots()
    {
        var day = Day(durationMinutes: 150);

        day.Slots.Select(s => s.Start).Should().Equal(At(9), At(9, 30));
    }

    [Fact]
    public void A_service_longer_than_the_opening_range_has_no_slot_at_all()
    {
        Day(durationMinutes: 240).Slots.Should().BeEmpty();
    }

    [Fact]
    public void A_booked_period_blocks_overlapping_slots_but_not_adjacent_ones()
    {
        var day = Day(busy: [new BusyPeriod(Anna, At(10), At(11))]);

        day.Slots.Where(s => !s.Available).Select(s => s.Start).Should().Equal(At(9, 30), At(10), At(10, 30));
        day.Slots.Single(s => s.Start == At(9)).Available.Should().BeTrue(); // ends exactly when the booking starts
        day.Slots.Single(s => s.Start == At(11)).Available.Should().BeTrue(); // starts exactly when it ends
    }

    [Fact]
    public void With_several_resources_a_slot_stays_free_while_one_of_them_is_free()
    {
        var day = Day(resources: [Anna, Bella], busy: [new BusyPeriod(Anna, At(10), At(11))]);

        var blockedForAnna = day.Slots.Single(s => s.Start == At(10));
        blockedForAnna.Available.Should().BeTrue();
        blockedForAnna.FreeResources.Should().Equal(Bella);
        day.Slots.Single(s => s.Start == At(9)).FreeResources.Should().Equal(Anna, Bella);
    }

    [Fact]
    public void Everyone_is_busy_means_the_slot_is_unavailable()
    {
        var day = Day(resources: [Anna, Bella], busy: [new BusyPeriod(Anna, At(10), At(11)), new BusyPeriod(Bella, At(10), At(11))]);

        day.Slots.Single(s => s.Start == At(10)).Available.Should().BeFalse();
    }

    [Fact]
    public void A_closure_of_the_whole_establishment_blocks_every_resource()
    {
        var day = Day(resources: [Anna, Bella], busy: [new BusyPeriod(null, At(9), At(12))]);

        day.Slots.Should().OnlyContain(s => !s.Available);
    }

    [Fact]
    public void A_closure_of_one_resource_leaves_the_others_bookable()
    {
        var day = Day(resources: [Anna, Bella], busy: [new BusyPeriod(Bella, At(9), At(12))]);

        day.Slots.Should().OnlyContain(s => s.Available && s.FreeResources.SequenceEqual(new[] { Anna }));
    }

    [Fact]
    public void The_minimum_lead_time_hides_slots_that_are_too_soon()
    {
        var day = Day(
            rules: new BookingRules(30, MinLeadMinutes: 120, MaxHorizonDays: 60),
            now: At(8, 30));

        // 08:30 + 2 h = 10:30: earlier slots are shown but unavailable.
        day.Slots.Where(s => s.Available).Select(s => s.Start).Should().Equal(At(10, 30), At(11));
        day.Slots.Should().HaveCount(5);
    }

    [Fact]
    public void Days_beyond_the_booking_horizon_are_unavailable()
    {
        var day = Day(
            rules: new BookingRules(30, 0, MaxHorizonDays: 5),
            now: new DateTimeOffset(2026, 10, 1, 8, 0, 0, TimeSpan.Zero));

        day.Slots.Should().NotBeEmpty().And.OnlyContain(s => !s.Available);
    }

    [Fact]
    public void Several_ranges_in_a_day_model_a_lunch_break()
    {
        var hours = Hours((DayOfWeek.Monday, "09:00", "10:00"), (DayOfWeek.Monday, "14:00", "15:00"));

        var day = Day(durationMinutes: 60, hours: hours, rules: new BookingRules(60, 0, 60));

        day.Slots.Select(s => s.Start).Should().Equal(At(9), At(14));
    }

    [Fact]
    public void A_day_without_opening_hours_is_closed()
    {
        var day = SlotCalculator.Compute(
            Monday, Monday, 60, Hours((DayOfWeek.Tuesday, "09:00", "12:00")), [Anna], [],
            new BookingRules(30, 0, 60), TimeZoneInfo.Utc, LongBefore).Single();

        day.IsOpen.Should().BeFalse();
        day.Slots.Should().BeEmpty();
    }

    [Fact]
    public void Opening_hours_are_local_times_converted_to_utc()
    {
        var plusOne = TimeZoneInfo.CreateCustomTimeZone("test", TimeSpan.FromHours(1), "test", "test");

        var day = Day(zone: plusOne);

        day.Slots[0].Start.Should().Be(At(8)); // 09:00 at UTC+1
    }

    [Fact]
    public void Every_slot_of_a_multi_day_range_is_returned_in_order()
    {
        var days = SlotCalculator.Compute(
            Monday, Monday.AddDays(2), 60,
            Hours((DayOfWeek.Monday, "09:00", "10:00"), (DayOfWeek.Wednesday, "09:00", "10:00")),
            [Anna], [], new BookingRules(60, 0, 60), TimeZoneInfo.Utc, LongBefore);

        days.Select(d => d.Date).Should().Equal(Monday, Monday.AddDays(1), Monday.AddDays(2));
        days.Select(d => d.IsOpen).Should().Equal(true, false, true);
    }

    [Theory]
    [InlineData(0, 30)]
    [InlineData(60, 0)]
    public void Invalid_parameters_yield_no_slots(int duration, int step)
    {
        SlotCalculator.Compute(
                Monday, Monday, duration, Hours((DayOfWeek.Monday, "09:00", "12:00")), [Anna], [],
                new BookingRules(step, 0, 60), TimeZoneInfo.Utc, LongBefore)
            .Should().BeEmpty();
    }
}
