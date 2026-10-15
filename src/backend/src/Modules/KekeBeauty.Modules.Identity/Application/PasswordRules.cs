using System.Net.Mail;

namespace KekeBeauty.Modules.Identity.Application;

public static class PasswordRules
{
    public const int MinLength = 10;
    public const int MaxLength = 128;

    /// <summary>Returns the human-readable (French) reasons a password is rejected; empty when acceptable.</summary>
    public static IReadOnlyList<string> Validate(string? password, string email)
    {
        var errors = new List<string>();
        var value = password ?? string.Empty;

        if (value.Length < MinLength)
        {
            errors.Add($"Le mot de passe doit contenir au moins {MinLength} caractères.");
        }

        if (value.Length > MaxLength)
        {
            errors.Add($"Le mot de passe ne doit pas dépasser {MaxLength} caractères.");
        }

        if (!value.Any(char.IsLetter) || !value.Any(char.IsDigit))
        {
            errors.Add("Le mot de passe doit contenir au moins une lettre et un chiffre.");
        }

        var localPart = email.Split('@')[0];
        if (localPart.Length >= 3 && value.Contains(localPart, StringComparison.OrdinalIgnoreCase))
        {
            errors.Add("Le mot de passe ne doit pas contenir votre adresse e-mail.");
        }

        return errors;
    }

    public static bool IsValidEmail(string? email)
    {
        if (string.IsNullOrWhiteSpace(email) || email.Length > 254)
        {
            return false;
        }

        return MailAddress.TryCreate(email, out var parsed)
            && parsed.Address == email
            && parsed.Host.Contains('.', StringComparison.Ordinal);
    }
}
