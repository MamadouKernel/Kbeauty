using System.Threading.RateLimiting;

namespace KekeBeauty.SharedKernel;

/// <summary>Thrown by a store that is deliberately not being asked (its circuit breaker is open): the caller counts alone, silently.</summary>
public sealed class RateLimitStoreSkippedException : Exception;

/// <summary>
/// A counter shared by every API instance (Redis). Without one, each instance counts alone and a limit of 30 a minute silently becomes
/// 30 a minute <i>per instance</i>.
/// </summary>
public interface ISharedRateLimitStore
{
    /// <summary>Counts one request in the window that contains now and returns the new total. Throws when the store cannot be reached.</summary>
    ValueTask<long> IncrementAsync(string key, TimeSpan window, CancellationToken cancellationToken);

    /// <summary>The same, synchronously: ASP.NET Core's rate-limit middleware first tries to acquire a permit without awaiting anything.</summary>
    long Increment(string key, TimeSpan window);
}

/// <summary>
/// Fixed-window rate limits that use the shared counter when there is one and the instance's own counter otherwise, or when the shared
/// store fails: an unreachable Redis must never take the site down, and must never remove the limits either.
/// </summary>
public static class SharedRateLimit
{
    /// <summary>
    /// One client, one counter: the same IPv4 address can arrive as 203.0.113.9 or as ::ffff:203.0.113.9 (dual-stack listener, proxy), and counting
    /// those as two clients would double the allowance.
    /// </summary>
    public static string NormalizeKey(string key) =>
        System.Net.IPAddress.TryParse(key, out var address) && address.IsIPv4MappedToIPv6 ? address.MapToIPv4().ToString() : key;

    public static RateLimitPartition<string> FixedWindow(IServiceProvider services, string policy, string key, int limit, TimeSpan window)
    {
        var store = (ISharedRateLimitStore?)services.GetService(typeof(ISharedRateLimitStore));
        var partitionKey = $"{policy}:{NormalizeKey(key)}";
        if (store is null)
        {
            return RateLimitPartition.GetFixedWindowLimiter(partitionKey, _ => new FixedWindowRateLimiterOptions { PermitLimit = limit, Window = window });
        }

        var time = (TimeProvider?)services.GetService(typeof(TimeProvider)) ?? TimeProvider.System;
        return new RateLimitPartition<string>(partitionKey, _ => new SharedFixedWindowLimiter(store, partitionKey, limit, window, time));
    }

    private sealed class SharedFixedWindowLimiter : RateLimiter
    {
        private readonly ISharedRateLimitStore _store;
        private readonly string _key;
        private readonly int _limit;
        private readonly TimeSpan _window;
        private readonly TimeProvider _time;
        private readonly FixedWindowRateLimiter _local;

        public SharedFixedWindowLimiter(ISharedRateLimitStore store, string key, int limit, TimeSpan window, TimeProvider time)
        {
            _store = store;
            _key = key;
            _limit = limit;
            _window = window;
            _time = time;
            _local = new FixedWindowRateLimiter(new FixedWindowRateLimiterOptions { PermitLimit = limit, Window = window, QueueLimit = 0 });
        }

        public override TimeSpan? IdleDuration => _local.IdleDuration;

        public override RateLimiterStatistics? GetStatistics() => _local.GetStatistics();

        private static void CountFailure(Exception ex)
        {
            if (ex is not RateLimitStoreSkippedException)
            {
                Telemetry.RateLimitStoreErrors.Add(1);
            }
        }

        private RateLimitLease Decide(long used)
        {
            if (used <= _limit)
            {
                return new Lease(true, null);
            }

            var elapsed = _time.GetUtcNow().ToUnixTimeMilliseconds() % (long)_window.TotalMilliseconds;
            return new Lease(false, TimeSpan.FromMilliseconds((long)_window.TotalMilliseconds - elapsed));
        }

        protected override RateLimitLease AttemptAcquireCore(int permitCount)
        {
            try
            {
                return Decide(_store.Increment(_key, _window));
            }
            catch (Exception ex)
            {
                CountFailure(ex);
                return _local.AttemptAcquire(permitCount);
            }
        }

        protected override async ValueTask<RateLimitLease> AcquireAsyncCore(int permitCount, CancellationToken cancellationToken)
        {
            try
            {
                return Decide(await _store.IncrementAsync(_key, _window, cancellationToken));
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                CountFailure(ex);
                return await _local.AcquireAsync(permitCount, cancellationToken);
            }
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                _local.Dispose();
            }

            base.Dispose(disposing);
        }
    }

    private sealed class Lease(bool acquired, TimeSpan? retryAfter) : RateLimitLease
    {
        public override bool IsAcquired => acquired;

        public override IEnumerable<string> MetadataNames => retryAfter is null ? [] : [MetadataName.RetryAfter.Name];

        public override bool TryGetMetadata(string metadataName, out object? metadata)
        {
            if (retryAfter is { } value && metadataName == MetadataName.RetryAfter.Name)
            {
                metadata = value;
                return true;
            }

            metadata = null;
            return false;
        }
    }
}
