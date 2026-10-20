using FluentAssertions;
using KekeBeauty.Modules.Identity.Application;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using OtpNet;

namespace KekeBeauty.UnitTests;

public class TotpServiceTests
{
    private static readonly DateTimeOffset Start = new(2026, 10, 11, 12, 0, 0, TimeSpan.Zero);

    private readonly FakeTimeProvider _time = new(Start);
    private readonly TotpService _service;

    public TotpServiceTests()
    {
        _service = new TotpService(new EphemeralDataProtectionProvider(), _time, Options.Create(new AuthOptions()));
    }

    private static string CodeAt(string secret, DateTimeOffset moment) =>
        new Totp(Base32Encoding.ToBytes(secret)).ComputeTotp(moment.UtcDateTime);

    [Fact]
    public void Accepts_the_current_code()
    {
        var secret = _service.GenerateSecret();

        _service.TryVerify(secret, CodeAt(secret, Start), null, out var step).Should().BeTrue();
        step.Should().BeGreaterThan(0);
    }

    [Fact]
    public void Rejects_a_wrong_or_malformed_code()
    {
        var secret = _service.GenerateSecret();
        var valid = CodeAt(secret, Start);
        var wrong = valid == "000000" ? "000001" : "000000";

        _service.TryVerify(secret, wrong, null, out _).Should().BeFalse();
        _service.TryVerify(secret, "12345", null, out _).Should().BeFalse();
        _service.TryVerify(secret, "abcdef", null, out _).Should().BeFalse();
    }

    [Fact]
    public void Rejects_a_replayed_step()
    {
        var secret = _service.GenerateSecret();
        var code = CodeAt(secret, Start);
        _service.TryVerify(secret, code, null, out var step).Should().BeTrue();

        _service.TryVerify(secret, code, step, out _).Should().BeFalse();
    }

    [Fact]
    public void Tolerates_one_step_of_clock_drift_but_not_two()
    {
        var secret = _service.GenerateSecret();

        _service.TryVerify(secret, CodeAt(secret, Start.AddSeconds(-30)), null, out _).Should().BeTrue();
        _service.TryVerify(secret, CodeAt(secret, Start.AddSeconds(30)), null, out _).Should().BeTrue();
        _service.TryVerify(secret, CodeAt(secret, Start.AddSeconds(-60)), null, out _).Should().BeFalse();
        _service.TryVerify(secret, CodeAt(secret, Start.AddSeconds(60)), null, out _).Should().BeFalse();
    }

    [Fact]
    public void Protects_the_secret_at_rest_and_round_trips_it()
    {
        var secret = _service.GenerateSecret();

        var protectedSecret = _service.Protect(secret);

        protectedSecret.Should().NotContain(secret);
        _service.Unprotect(protectedSecret).Should().Be(secret);
    }

    [Fact]
    public void Builds_an_otpauth_uri_for_authenticator_apps()
    {
        var secret = _service.GenerateSecret();
        var uri = _service.BuildOtpauthUri("awa@example.com", secret);

        uri.Should().StartWith("otpauth://totp/Keke%20Beauty:awa%40example.com?");
        uri.Should().Contain($"secret={secret}").And.Contain("issuer=Keke%20Beauty").And.Contain("digits=6").And.Contain("period=30");
    }

    [Fact]
    public void Trusted_device_remains_active_until_expiry_or_revocation()
    {
        var device = KekeBeauty.Modules.Identity.Domain.TrustedDevice.Create(
            Guid.CreateVersion7(),
            "hash-123",
            Start,
            Start.AddDays(30),
            "Chrome / Linux",
            "127.0.0.1");

        device.IsActive(Start).Should().BeTrue();
        device.IsActive(Start.AddDays(29)).Should().BeTrue();
        device.IsActive(Start.AddDays(30)).Should().BeFalse();

        var touchedAt = Start.AddDays(5);
        device.Touch(touchedAt, "10.0.0.2");
        device.LastUsedAt.Should().Be(touchedAt);
        device.IpAddress.Should().Be("10.0.0.2");

        device.Revoke(Start.AddDays(6));
        device.IsActive(Start.AddDays(6)).Should().BeFalse();
    }

    [Fact]
    public void AppRoles_exposes_all_fine_grained_admin_roles()
    {
        KekeBeauty.Modules.Identity.Domain.AppRoles.AllAdminRoles.Should().BeEquivalentTo(
            ["SuperAdmin", "Admin", "AdminKyc", "AdminSupport", "AdminAccountant"]);
        KekeBeauty.Modules.Identity.Domain.AppRoles.DelegatedAdminRoles.Should().BeEquivalentTo(
            ["Admin", "AdminKyc", "AdminSupport", "AdminAccountant"]);
    }
}
