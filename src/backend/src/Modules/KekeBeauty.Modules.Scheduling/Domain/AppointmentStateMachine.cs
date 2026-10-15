namespace KekeBeauty.Modules.Scheduling.Domain;

public enum ActorKind
{
    Client,
    Partner,
    System,
}

/// <summary>Allowed appointment transitions and who may trigger them (PRD §7.4 state diagram).</summary>
public static class AppointmentStateMachine
{
    private static readonly Dictionary<(AppointmentStatus From, AppointmentStatus To), ActorKind[]> Allowed = new()
    {
        [(AppointmentStatus.Pending, AppointmentStatus.Confirmed)] = [ActorKind.Partner, ActorKind.System],
        [(AppointmentStatus.Pending, AppointmentStatus.Rejected)] = [ActorKind.Partner],
        [(AppointmentStatus.Pending, AppointmentStatus.Expired)] = [ActorKind.System],
        [(AppointmentStatus.Pending, AppointmentStatus.CancelledByClient)] = [ActorKind.Client],
        [(AppointmentStatus.Pending, AppointmentStatus.CancelledByEstablishment)] = [ActorKind.Partner],
        [(AppointmentStatus.Pending, AppointmentStatus.RescheduleProposed)] = [ActorKind.Partner],
        [(AppointmentStatus.Confirmed, AppointmentStatus.Completed)] = [ActorKind.System],
        [(AppointmentStatus.Confirmed, AppointmentStatus.NoShow)] = [ActorKind.Partner],
        [(AppointmentStatus.Confirmed, AppointmentStatus.RescheduleProposed)] = [ActorKind.Partner],
        [(AppointmentStatus.Confirmed, AppointmentStatus.CancelledByClient)] = [ActorKind.Client],
        [(AppointmentStatus.Confirmed, AppointmentStatus.CancelledByEstablishment)] = [ActorKind.Partner],
        [(AppointmentStatus.RescheduleProposed, AppointmentStatus.Confirmed)] = [ActorKind.Client],
        [(AppointmentStatus.RescheduleProposed, AppointmentStatus.CancelledByClient)] = [ActorKind.Client, ActorKind.System],
        [(AppointmentStatus.RescheduleProposed, AppointmentStatus.CancelledByEstablishment)] = [ActorKind.Partner],
    };

    /// <summary>Statuses that hold a slot: the database exclusion constraint applies to exactly these.</summary>
    public static readonly IReadOnlyList<AppointmentStatus> ActiveStatuses =
        [AppointmentStatus.Pending, AppointmentStatus.Confirmed, AppointmentStatus.RescheduleProposed];

    public static bool IsActive(AppointmentStatus status) => ActiveStatuses.Contains(status);

    public static bool CanTransition(AppointmentStatus from, AppointmentStatus to, ActorKind actor) =>
        Allowed.TryGetValue((from, to), out var actors) && actors.Contains(actor);
}
