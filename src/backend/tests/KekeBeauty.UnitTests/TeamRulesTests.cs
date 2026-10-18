using FluentAssertions;
using KekeBeauty.Infrastructure.Scheduling;
using KekeBeauty.Modules.Scheduling.Domain;

namespace KekeBeauty.UnitTests;

public sealed class TeamRulesTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 18, 10, 0, 0, TimeSpan.Zero);

    private static Appointment Booking(AppointmentStatus status, TimeSpan startsIn, Action<Appointment>? tweak = null)
    {
        var appointment = new Appointment
        {
            EstablishmentId = Guid.NewGuid(),
            ResourceId = Guid.NewGuid(),
            StartsAt = Now + startsIn,
            EndsAt = Now + startsIn + TimeSpan.FromHours(1),
            Status = status,
            TotalPrice = 10_000m,
        };
        tweak?.Invoke(appointment);
        return appointment;
    }

    [Theory]
    [InlineData(10_000, 30, 3_000)]
    [InlineData(10_000, 0, 0)]
    [InlineData(10_000, 100, 10_000)]
    [InlineData(2_550, 33.33, 850)]
    [InlineData(1_005, 50, 503)]
    public void Commission_is_a_rounded_share_of_the_price(double price, double percent, double expected) =>
        CommissionService.CommissionOf((decimal)price, (decimal)percent).Should().Be((decimal)expected);

    [Fact]
    public void A_treatment_can_start_slightly_before_its_slot_but_not_a_day_ahead()
    {
        TeamService.CanStart(Booking(AppointmentStatus.Confirmed, TimeSpan.FromMinutes(20)), Now).Should().BeTrue();
        TeamService.CanStart(Booking(AppointmentStatus.Confirmed, TimeSpan.FromMinutes(-15)), Now).Should().BeTrue();
        TeamService.CanStart(Booking(AppointmentStatus.Confirmed, TimeSpan.FromHours(3)), Now).Should().BeFalse();
        TeamService.CanStart(Booking(AppointmentStatus.Confirmed, TimeSpan.FromDays(1)), Now).Should().BeFalse();
    }

    [Fact]
    public void A_past_appointment_cannot_be_started_long_after_it_ended()
    {
        TeamService.CanStart(Booking(AppointmentStatus.Confirmed, TimeSpan.FromMinutes(-90)), Now).Should().BeTrue();
        TeamService.CanStart(Booking(AppointmentStatus.Confirmed, TimeSpan.FromHours(-6)), Now).Should().BeFalse();
        TeamService.CanStart(Booking(AppointmentStatus.Confirmed, TimeSpan.FromDays(-30)), Now).Should().BeFalse();
    }

    [Theory]
    [InlineData(AppointmentStatus.Pending)]
    [InlineData(AppointmentStatus.Completed)]
    [InlineData(AppointmentStatus.NoShow)]
    [InlineData(AppointmentStatus.CancelledByClient)]
    public void Only_a_confirmed_appointment_can_be_started(AppointmentStatus status) =>
        TeamService.CanStart(Booking(status, TimeSpan.Zero), Now).Should().BeFalse();

    [Fact]
    public void A_started_or_unpaid_appointment_cannot_be_started_again()
    {
        TeamService.CanStart(Booking(AppointmentStatus.Confirmed, TimeSpan.Zero, a => a.StartedAt = Now), Now).Should().BeFalse();
        TeamService.CanStart(Booking(AppointmentStatus.Confirmed, TimeSpan.Zero, a => a.AwaitingPayment = true), Now).Should().BeFalse();
    }

    [Fact]
    public void A_service_counts_as_finished_when_completed_or_when_the_timer_was_stopped()
    {
        CommissionService.IsFinished(Booking(AppointmentStatus.Completed, TimeSpan.Zero)).Should().BeTrue();
        CommissionService.IsFinished(Booking(AppointmentStatus.Confirmed, TimeSpan.Zero, a => a.FinishedAt = Now)).Should().BeTrue();
        CommissionService.IsFinished(Booking(AppointmentStatus.Confirmed, TimeSpan.Zero)).Should().BeFalse();
        CommissionService.IsFinished(Booking(AppointmentStatus.NoShow, TimeSpan.Zero, a => a.FinishedAt = Now)).Should().BeFalse();
    }
}
