using FluentAssertions;
using KekeBeauty.Modules.Scheduling.Domain;

namespace KekeBeauty.UnitTests;

public sealed class AppointmentStateMachineTests
{
    private static readonly AppointmentStatus[] Terminal =
    [
        AppointmentStatus.Rejected,
        AppointmentStatus.Expired,
        AppointmentStatus.CancelledByClient,
        AppointmentStatus.CancelledByEstablishment,
        AppointmentStatus.Completed,
        AppointmentStatus.NoShow,
    ];

    [Theory]
    [InlineData(AppointmentStatus.Pending, AppointmentStatus.Confirmed, ActorKind.Partner)]
    [InlineData(AppointmentStatus.Pending, AppointmentStatus.Confirmed, ActorKind.System)]
    [InlineData(AppointmentStatus.Pending, AppointmentStatus.Rejected, ActorKind.Partner)]
    [InlineData(AppointmentStatus.Pending, AppointmentStatus.Expired, ActorKind.System)]
    [InlineData(AppointmentStatus.Pending, AppointmentStatus.CancelledByClient, ActorKind.Client)]
    [InlineData(AppointmentStatus.Confirmed, AppointmentStatus.Completed, ActorKind.System)]
    [InlineData(AppointmentStatus.Confirmed, AppointmentStatus.NoShow, ActorKind.Partner)]
    [InlineData(AppointmentStatus.Confirmed, AppointmentStatus.RescheduleProposed, ActorKind.Partner)]
    [InlineData(AppointmentStatus.Confirmed, AppointmentStatus.CancelledByClient, ActorKind.Client)]
    [InlineData(AppointmentStatus.Confirmed, AppointmentStatus.CancelledByEstablishment, ActorKind.Partner)]
    [InlineData(AppointmentStatus.RescheduleProposed, AppointmentStatus.Confirmed, ActorKind.Client)]
    [InlineData(AppointmentStatus.RescheduleProposed, AppointmentStatus.CancelledByClient, ActorKind.Client)]
    [InlineData(AppointmentStatus.RescheduleProposed, AppointmentStatus.CancelledByClient, ActorKind.System)]
    public void Allowed_transitions(AppointmentStatus from, AppointmentStatus to, ActorKind actor) =>
        AppointmentStateMachine.CanTransition(from, to, actor).Should().BeTrue();

    [Theory]
    [InlineData(AppointmentStatus.Pending, AppointmentStatus.Confirmed, ActorKind.Client)] // a client cannot confirm their own request
    [InlineData(AppointmentStatus.Pending, AppointmentStatus.Rejected, ActorKind.Client)]
    [InlineData(AppointmentStatus.Pending, AppointmentStatus.Expired, ActorKind.Partner)] // only time expires a request
    [InlineData(AppointmentStatus.Confirmed, AppointmentStatus.Completed, ActorKind.Partner)]
    [InlineData(AppointmentStatus.Confirmed, AppointmentStatus.Pending, ActorKind.System)]
    [InlineData(AppointmentStatus.Confirmed, AppointmentStatus.Rejected, ActorKind.Partner)]
    [InlineData(AppointmentStatus.RescheduleProposed, AppointmentStatus.Confirmed, ActorKind.Partner)] // only the client accepts
    [InlineData(AppointmentStatus.RescheduleProposed, AppointmentStatus.NoShow, ActorKind.Partner)]
    public void Forbidden_transitions(AppointmentStatus from, AppointmentStatus to, ActorKind actor) =>
        AppointmentStateMachine.CanTransition(from, to, actor).Should().BeFalse();

    [Fact]
    public void Terminal_statuses_never_change_again()
    {
        var statuses = Enum.GetValues<AppointmentStatus>();
        var actors = Enum.GetValues<ActorKind>();

        foreach (var from in Terminal)
        {
            statuses.SelectMany(to => actors.Select(actor => AppointmentStateMachine.CanTransition(from, to, actor)))
                .Should().OnlyContain(allowed => !allowed, $"{from} is final");
        }
    }

    [Fact]
    public void Only_pending_confirmed_and_proposed_appointments_hold_a_slot()
    {
        AppointmentStateMachine.ActiveStatuses.Should().BeEquivalentTo(
            [AppointmentStatus.Pending, AppointmentStatus.Confirmed, AppointmentStatus.RescheduleProposed]);
        Terminal.Should().OnlyContain(status => !AppointmentStateMachine.IsActive(status));
    }

    [Fact]
    public void Nothing_transitions_to_itself()
    {
        foreach (var status in Enum.GetValues<AppointmentStatus>())
        {
            Enum.GetValues<ActorKind>().Should().OnlyContain(actor => !AppointmentStateMachine.CanTransition(status, status, actor));
        }
    }
}
