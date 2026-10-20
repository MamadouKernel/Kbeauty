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

        if (IsTooCommon(value))
        {
            errors.Add("Ce mot de passe est trop courant : choisissez-en un moins prévisible.");
        }

        var localPart = email.Split('@')[0];
        if (localPart.Length >= 3 && value.Contains(localPart, StringComparison.OrdinalIgnoreCase))
        {
            errors.Add("Le mot de passe ne doit pas contenir votre adresse e-mail.");
        }

        return errors;
    }

    /// <summary>
    /// The most guessed words and keyboard patterns, in English and French. A password is refused when, once its digits and symbols are
    /// taken away, what is left is one of them: "Password2026!" and "azerty123456" are exactly what a guessing attack tries first.
    /// </summary>
    private static readonly HashSet<string> CommonCores = new(StringComparer.Ordinal)
    {
        "password", "passwort", "motdepasse", "monmotdepasse", "mdp", "azerty", "azertyuiop", "qwerty", "qwertyuiop", "qwertz", "abcdef",
        "abcdefgh", "abcdefghij", "azertyui", "welcome", "bienvenue", "bonjour", "bonsoir", "admin", "administrator", "administrateur", "root",
        "letmein", "login", "master", "dragon", "monkey", "football", "soccer", "iloveyou", "jetaime", "loveyou", "sunshine", "princess",
        "superman", "batman", "shadow", "trustno", "changeme", "changemoi", "secret", "secretpassword", "keke", "kekebeauty", "beauty",
        "abidjan", "cotedivoire", "ivoire", "coiffure", "salon", "azertyazerty", "qwertyqwerty", "aaaaaaaaaa", "testtest", "testing", "test",
        "iloveyoutoo", "passw0rd", "p4ssword", "mypassword", "monpassword", "motdepasse", "bismillah", "inshallah", "chocolate", "freedom",
    };

    private static bool IsTooCommon(string password)
    {
        var core = new string(password.Where(char.IsLetter).ToArray()).ToLowerInvariant();
        if (CommonCores.Contains(core))
        {
            return true;
        }

        // The classic substitutions (p4ssw0rd, P@ssword, m0td3p@ss3) fool a plain letter filter: undo them and look again, with and without the
        // digits and symbols that people put around a word ("Passw0rd12345": the trailing digits must not be turned into letters too).
        static string Undo(string value) => new(value.ToLowerInvariant().Select(c => c switch
        {
            '0' => 'o',
            '1' => 'i',
            '3' => 'e',
            '4' => 'a',
            '5' => 's',
            '7' => 't',
            '@' => 'a',
            '$' => 's',
            '!' => 'i',
            _ => c,
        }).Where(char.IsLetter).ToArray());

        if (CommonCores.Contains(Undo(password)) || CommonCores.Contains(Undo(password.Trim(password.Where(c => !char.IsLetter(c)).Distinct().ToArray()))))
        {
            return true;
        }

        // One character repeated, or a plain run such as 1234567890 / abcdefghij: nothing to guess.
        var distinct = password.ToLowerInvariant().Distinct().Count();
        return distinct <= 2 || "0123456789".Contains(password, StringComparison.Ordinal) || "abcdefghijklmnopqrstuvwxyz".Contains(password.ToLowerInvariant(), StringComparison.Ordinal);
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
