namespace KekeBeauty.Application.Rdv;

public sealed class CreneauOccupe
{
    public DateTimeOffset DateHeureDebut { get; set; }
    public DateTimeOffset DateHeureFin { get; set; }
}

public sealed record RequestRdvResult(bool Success, string Status, Guid? IdRdv = null);

public sealed record DecideRdvResult(bool Success, string Status);
