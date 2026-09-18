namespace KekeBeauty.Application.Health;

public sealed record HealthDataCheckResult(bool CanRead, bool CanWrite, string? ErrorMessage = null)
{
    public bool IsHealthy => CanRead && CanWrite;
}
