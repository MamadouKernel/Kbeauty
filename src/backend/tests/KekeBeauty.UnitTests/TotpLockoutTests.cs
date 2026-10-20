using FluentAssertions;
using KekeBeauty.Modules.Identity.Domain;

namespace KekeBeauty.UnitTests;

/// <summary>The second factor locks per account: guessing 6-digit codes must stay hopeless even for someone who knows the password.</summary>
public sealed class TotpLockoutTests
{
    private static readonly DateTimeOffset Start = new(2026, 10, 20, 10, 0, 0, TimeSpan.Zero);
    private static readonly TimeSpan BaseLock = TimeSpan.FromMinutes(15);
    private static readonly TimeSpan MaxLock = TimeSpan.FromHours(24);

    private static User NewUser() => User.Register("awa@example.com", "Awa", "Koné", "Client", emailConfirmed: true, Start);

    private static void Fail(User user, DateTimeOffset now, int times)
    {
        for (var i = 0; i < times; i++)
        {
            user.RegisterFailedTotp(now, 5, BaseLock, MaxLock);
        }
    }

    [Fact]
    public void Four_wrong_codes_do_not_lock_but_the_fifth_does()
    {
        var user = NewUser();

        Fail(user, Start, 4);
        user.IsTotpLocked(Start).Should().BeFalse();

        Fail(user, Start, 1);
        user.IsTotpLocked(Start).Should().BeTrue();
        user.IsTotpLocked(Start + BaseLock - TimeSpan.FromSeconds(1)).Should().BeTrue();
        user.IsTotpLocked(Start + BaseLock).Should().BeFalse();
    }

    [Fact]
    public void Each_new_lock_lasts_twice_as_long_up_to_the_ceiling()
    {
        var user = NewUser();
        var now = Start;
        var expected = new[] { 15, 30, 60, 120, 240, 480, 960, 1440, 1440 };

        foreach (var minutes in expected)
        {
            Fail(user, now, 5);
            user.TotpLockedUntil.Should().Be(now + TimeSpan.FromMinutes(minutes));
            now = user.TotpLockedUntil!.Value;
        }
    }

    [Fact]
    public void A_successful_code_starts_again_from_zero()
    {
        var user = NewUser();
        Fail(user, Start, 5);
        Fail(user, Start.AddMinutes(20), 5);
        user.TotpLockoutLevel.Should().Be(2);

        user.ResetTotpFailures();

        user.IsTotpLocked(Start.AddMinutes(21)).Should().BeFalse();
        user.TotpFailedCount.Should().Be(0);
        user.TotpLockoutLevel.Should().Be(0);
        Fail(user, Start.AddHours(2), 5);
        user.TotpLockedUntil.Should().Be(Start.AddHours(2) + BaseLock, "the first lock after a success is the short one again");
    }

    [Fact]
    public void The_password_lock_and_the_second_factor_lock_are_independent()
    {
        var user = NewUser();

        Fail(user, Start, 5);

        user.IsTotpLocked(Start).Should().BeTrue();
        user.IsLocked(Start).Should().BeFalse("only the second factor is locked: the password is not");
    }
}
