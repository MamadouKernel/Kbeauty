using FluentAssertions;
using KekeBeauty.Modules.Identity.Domain;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;

namespace KekeBeauty.IntegrationTests;

[Collection(ApiCollection.Name)]
public sealed class PasswordHashingTests(ApiFixture api)
{
    [Fact]
    public void New_password_hashes_use_at_least_the_iteration_count_the_owasp_guidance_asks_for()
    {
        var hasher = api.Services.GetRequiredService<IPasswordHasher<User>>();
        var user = User.Register("hash@example.com", "A", "B", "Client", emailConfirmed: true, DateTimeOffset.UtcNow);

        var hash = hasher.HashPassword(user, TestSecrets.Password);

        // ASP.NET Core Identity v3 format: version byte, then PRF (4 bytes) and the iteration count (4 bytes, big-endian).
        var bytes = Convert.FromBase64String(hash);
        bytes[0].Should().Be(0x01);
        var iterations = (bytes[5] << 24) | (bytes[6] << 16) | (bytes[7] << 8) | bytes[8];
        iterations.Should().BeGreaterThanOrEqualTo(210_000);
        hasher.VerifyHashedPassword(user, hash, TestSecrets.Password).Should().Be(PasswordVerificationResult.Success);
    }

    private static int IterationsOf(string hash)
    {
        var bytes = Convert.FromBase64String(hash);
        return (bytes[5] << 24) | (bytes[6] << 16) | (bytes[7] << 8) | bytes[8];
    }

    [Theory]
    [InlineData("1000", 210_000)]
    [InlineData("300000", 300_000)]
    public void The_iteration_count_is_configurable_but_never_below_the_owasp_floor(string configured, int expected)
    {
        using var host = api.WithWebHostBuilder(builder => builder.UseSetting("Auth:PasswordHashIterations", configured));
        var hasher = host.Services.GetRequiredService<IPasswordHasher<User>>();
        var user = User.Register("floor@example.com", "A", "B", "Client", emailConfirmed: true, DateTimeOffset.UtcNow);

        IterationsOf(hasher.HashPassword(user, TestSecrets.Password)).Should().Be(expected);
    }
}
