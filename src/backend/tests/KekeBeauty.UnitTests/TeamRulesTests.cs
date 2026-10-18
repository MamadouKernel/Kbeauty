using FluentAssertions;
using KekeBeauty.Infrastructure.Billing;
using KekeBeauty.Infrastructure.Scheduling;
using KekeBeauty.Modules.Billing.Domain;
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

public sealed class LateFeeRulesTests
{
    private static AppointmentPayment Paid(decimal amount, decimal platformFee) => new()
    {
        AppointmentId = Guid.NewGuid(),
        EstablishmentId = Guid.NewGuid(),
        ClientUserId = Guid.NewGuid(),
        Kind = AppointmentPaymentKind.Full,
        Amount = amount,
        PlatformFee = platformFee,
        NetAmount = amount - platformFee,
    };

    [Theory]
    [InlineData(10_000, 30, 10_000, 3_000)]
    [InlineData(10_000, 30, 2_000, 2_000)]
    [InlineData(10_000, 0, 10_000, 0)]
    [InlineData(3_333, 10, 3_333, 334)]
    [InlineData(10_000, 100, 10_000, 10_000)]
    public void The_fee_is_a_share_of_the_price_never_above_what_was_paid(double total, int percent, double paid, double expected) =>
        PaymentRules.LateFee((decimal)total, percent, (decimal)paid).Should().Be((decimal)expected);

    [Fact]
    public void A_late_cancellation_splits_the_payment_between_the_client_and_the_establishment()
    {
        var retention = PaymentRules.Retention(Paid(10_000, 500), 10_000, 30)!;

        retention.Fee.Should().Be(3_000);
        retention.Refund.Should().Be(7_000);
        retention.RetainedNet.Should().Be(2_850, "the platform keeps its 5 % of the fee");
        (retention.Refund + retention.Fee).Should().Be(10_000);
    }

    [Fact]
    public void Nothing_is_kept_without_a_fee_percent()
    {
        PaymentRules.Retention(Paid(10_000, 0), 10_000, 0).Should().BeNull();
    }

    [Fact]
    public void A_deposit_smaller_than_the_fee_is_kept_whole()
    {
        var retention = PaymentRules.Retention(Paid(3_000, 0), 10_000, 50)!;

        retention.Fee.Should().Be(3_000);
        retention.Refund.Should().Be(0);
    }
}
