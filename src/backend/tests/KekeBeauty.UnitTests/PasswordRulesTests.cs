using FluentAssertions;
using KekeBeauty.Modules.Identity.Application;

namespace KekeBeauty.UnitTests;

public class PasswordRulesTests
{
    private const string Email = "awa.kone@example.com";

    [Fact]
    public void Accepts_a_long_password_with_letters_and_digits()
    {
        PasswordRules.Validate("Zx-" + Guid.NewGuid().ToString("N")[..10] + "7", Email).Should().BeEmpty();
    }

    [Fact]
    public void Rejects_a_short_password()
    {
        PasswordRules.Validate("Ab1", Email).Should().Contain(e => e.Contains("au moins 10"));
    }

    [Theory]
    [InlineData("abcdefghijklmnop")]
    [InlineData("1234567890123456")]
    public void Requires_both_a_letter_and_a_digit(string password)
    {
        PasswordRules.Validate(password, Email).Should().Contain(e => e.Contains("une lettre et un chiffre"));
    }

    [Fact]
    public void Rejects_a_password_containing_the_email_name()
    {
        PasswordRules.Validate("my-awa.kone-2026", Email).Should().Contain(e => e.Contains("adresse e-mail"));
    }

    [Fact]
    public void Rejects_an_excessively_long_password()
    {
        PasswordRules.Validate(new string('a', 129) + "1", Email).Should().Contain(e => e.Contains("128"));
    }

    [Theory]
    [InlineData("awa@example.com", true)]
    [InlineData("a.b+tag@sub.example.ci", true)]
    [InlineData("not-an-email", false)]
    [InlineData("missing@tld", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    public void Validates_email_addresses(string? email, bool expected)
    {
        PasswordRules.IsValidEmail(email).Should().Be(expected);
    }

    [Theory]
    [InlineData("Password2026!")]
    [InlineData("password123456")]
    [InlineData("Azerty123456")]
    [InlineData("MotDePasse2026")]
    [InlineData("Qwertyuiop1")]
    [InlineData("KekeBeauty2026!")]
    [InlineData("Passw0rd12345")]
    public void Common_passwords_are_refused_even_with_digits_and_symbols_added(string password) =>
        PasswordRules.Validate(password, Email).Should().Contain(reason => reason.Contains("trop courant"));

    [Fact]
    public void An_unpredictable_password_of_the_same_shape_is_accepted() =>
        PasswordRules.Validate("Zx-" + Guid.NewGuid().ToString("N")[..10] + "7", Email).Should().BeEmpty();
}
