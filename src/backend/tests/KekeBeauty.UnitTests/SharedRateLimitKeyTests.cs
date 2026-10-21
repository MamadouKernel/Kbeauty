using FluentAssertions;
using KekeBeauty.SharedKernel;

namespace KekeBeauty.UnitTests;

public sealed class SharedRateLimitKeyTests
{
    [Theory]
    [InlineData("203.0.113.9", "203.0.113.9")]
    [InlineData("::ffff:203.0.113.9", "203.0.113.9")]
    [InlineData("2001:db8::1", "2001:db8::1")]
    [InlineData("unknown", "unknown")]
    [InlineData("user-guid-123", "user-guid-123")]
    public void The_same_client_always_lands_in_the_same_counter(string key, string expected) =>
        SharedRateLimit.NormalizeKey(key).Should().Be(expected);
}
