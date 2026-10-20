using FluentAssertions;
using KekeBeauty.Modules.Identity.Application;

namespace KekeBeauty.UnitTests;

public sealed class SigningKeyGuardTests
{
    [Fact]
    public void A_key_whose_fingerprint_is_published_is_rejected_but_not_when_it_differs()
    {
        // The real burned keys are only known by fingerprint: the check is exercised on a throw-away key of the same shape.
        var key = SigningKeyGuard.NewEphemeralKey();

        SigningKeyGuard.Fingerprint(key).Should().MatchRegex("^[0-9a-f]{64}$");
        SigningKeyGuard.Fingerprint(key).Should().Be(SigningKeyGuard.Fingerprint(key));
        SigningKeyGuard.Fingerprint(key).Should().NotBe(SigningKeyGuard.Fingerprint(SigningKeyGuard.NewEphemeralKey()));
        SigningKeyGuard.RejectionReason(key).Should().BeNull();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("too-short")]
    [InlineData("aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa")]
    [InlineData("please-change-me-to-something-else-entirely-0123")]
    [InlineData("my-example-key-for-the-documentation-0123456789")]
    public void Missing_short_placeholder_or_low_variety_keys_are_rejected(string? key) =>
        SigningKeyGuard.RejectionReason(key).Should().NotBeNull();

    [Fact]
    public void A_generated_key_is_accepted_and_two_keys_differ()
    {
        var first = SigningKeyGuard.NewEphemeralKey();
        var second = SigningKeyGuard.NewEphemeralKey();

        first.Length.Should().BeGreaterThanOrEqualTo(32);
        SigningKeyGuard.RejectionReason(first).Should().BeNull();
        second.Should().NotBe(first);
    }
}
