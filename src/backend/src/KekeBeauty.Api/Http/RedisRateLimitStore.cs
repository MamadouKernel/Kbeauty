using KekeBeauty.SharedKernel;
using StackExchange.Redis;

namespace KekeBeauty.Api.Http;

/// <summary>
/// The rate-limit counters, kept in Redis so that every API instance counts the same requests. One atomic script per request: it
/// increments the counter of the current window and gives the key a lifetime on the first hit, so a crash cannot leave a counter behind.
/// </summary>
public sealed class RedisRateLimitStore(IConnectionMultiplexer redis, TimeProvider time) : ISharedRateLimitStore
{
    private const string Script = """
        local count = redis.call('INCR', @key)
        if count == 1 then redis.call('PEXPIRE', @key, @ms) end
        return count
        """;

    private static readonly LuaScript Prepared = LuaScript.Prepare(Script);

    // After a failure, stop asking Redis for a few seconds: a Redis that is down must cost one slow request, not one per request.
    private static readonly TimeSpan SkipAfterFailure = TimeSpan.FromSeconds(10);
    private long _skipUntilTicks;

    private (RedisKey Key, long Ms) Prepare(string key, TimeSpan window)
    {
        if (time.GetUtcNow().UtcTicks < Interlocked.Read(ref _skipUntilTicks))
        {
            throw new RateLimitStoreSkippedException();
        }

        var windowMs = (long)window.TotalMilliseconds;
        // The window index is part of the key: every instance agrees on which window a request is in as long as the clocks are
        // NTP-synchronised, as any production host's are. The key outlives its window by a second so that no counter is ever left behind.
        var index = time.GetUtcNow().ToUnixTimeMilliseconds() / windowMs;
        return (new RedisKey($"keke:rl:{key}:{index}"), windowMs + 1000);
    }

    public async ValueTask<long> IncrementAsync(string key, TimeSpan window, CancellationToken cancellationToken)
    {
        var (redisKey, ms) = Prepare(key, window);
        try
        {
            return (long)await redis.GetDatabase().ScriptEvaluateAsync(Prepared, new { key = redisKey, ms });
        }
        catch (Exception)
        {
            Interlocked.Exchange(ref _skipUntilTicks, (time.GetUtcNow() + SkipAfterFailure).UtcTicks);
            throw;
        }
    }

    public long Increment(string key, TimeSpan window)
    {
        var (redisKey, ms) = Prepare(key, window);
        try
        {
            return (long)redis.GetDatabase().ScriptEvaluate(Prepared, new { key = redisKey, ms });
        }
        catch (Exception)
        {
            Interlocked.Exchange(ref _skipUntilTicks, (time.GetUtcNow() + SkipAfterFailure).UtcTicks);
            throw;
        }
    }
}
