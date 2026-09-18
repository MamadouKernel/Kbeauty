namespace KekeBeauty.Application.Health;

public interface IHealthDataCheck
{
    Task<HealthDataCheckResult> CheckAsync(CancellationToken cancellationToken);
}
