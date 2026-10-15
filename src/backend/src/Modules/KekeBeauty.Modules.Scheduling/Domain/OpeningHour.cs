using KekeBeauty.SharedKernel;

namespace KekeBeauty.Modules.Scheduling.Domain;

/// <summary>
/// One opening range of an establishment on a weekday (PRD §8.2). Several ranges per day are allowed
/// (e.g. a lunch break). <see cref="EstablishmentId"/> is a plain identifier: no cross-module navigation.
/// </summary>
public sealed class OpeningHour : Entity
{
    public required Guid EstablishmentId { get; init; }
    public required DayOfWeek DayOfWeek { get; init; }
    public required TimeOnly OpensAt { get; init; }
    public required TimeOnly ClosesAt { get; init; }
}
