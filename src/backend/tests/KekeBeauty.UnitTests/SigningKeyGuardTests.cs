using FluentAssertions;
using KekeBeauty.Modules.Identity.Application;

namespace KekeBeauty.UnitTests;

public sealed class SigningKeyGuardTests
{
    [Theory]
    [InlineData("dev-only-signing-key-change-me-0123456789abcdef")]
    [InlineData("integration-tests-signing-key-0123456789abcdef")]
    public void Keys_published_in_the_public_repository_are_never_acceptable(string key) =>
        SigningKeyGuard.RejectionReason(key).Should().NotBeNull();

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
