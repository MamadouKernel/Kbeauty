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
        var uri = _service.BuildOtpauthUri("awa@example.com", "JBSWY3DPEHPK3PXP");

        uri.Should().StartWith("otpauth://totp/Keke%20Beauty:awa%40example.com?");
        uri.Should().Contain("secret=JBSWY3DPEHPK3PXP").And.Contain("issuer=Keke%20Beauty").And.Contain("digits=6").And.Contain("period=30");
    }
}
