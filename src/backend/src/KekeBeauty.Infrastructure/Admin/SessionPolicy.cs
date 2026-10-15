using KekeBeauty.Modules.Identity.Application;

namespace KekeBeauty.Infrastructure.Admin;

/// <summary>Session inactivity limit read from the admin settings (default one hour).</summary>
public sealed class SessionPolicy(AppSettingsService settings) : ISessionPolicy
{
    public Task<TimeSpan> IdleTimeoutAsync(CancellationToken cancellationToken = default) => settings.SessionIdleAsync(cancellationToken);
}
