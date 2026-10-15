namespace KekeBeauty.Modules.Identity.Application;

/// <summary>How long a signed-in session may stay inactive before the next refresh is refused (an admin setting).</summary>
public interface ISessionPolicy
{
    Task<TimeSpan> IdleTimeoutAsync(CancellationToken cancellationToken = default);
}

/// <summary>Fallback when the host does not provide the admin-configurable policy: one hour of inactivity.</summary>
public sealed class DefaultSessionPolicy : ISessionPolicy
{
    public Task<TimeSpan> IdleTimeoutAsync(CancellationToken cancellationToken = default) => Task.FromResult(TimeSpan.FromMinutes(60));
}
