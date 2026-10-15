using System.Text;
using System.Text.RegularExpressions;
using KekeBeauty.Modules.Onboarding.Domain;

namespace KekeBeauty.Infrastructure.Partner;

/// <summary>
/// Text helpers (slug, name comparison, document-number normalisation). Accents are transliterated with an explicit
/// table because the API runs with invariant globalization, where Unicode normalisation is not guaranteed.
/// </summary>
public static partial class PartnerText
{
    private const string Accented = "àáâãäåçèéêëìíîïñòóôõöùúûüýÿ";
    private const string Plain = "aaaaaaceeeeiiiinooooouuuuyy";

    [GeneratedRegex(@"[^a-z0-9]+")]
    private static partial Regex NonAlphanumeric();

    [GeneratedRegex(@"^\+?[0-9][0-9 .\-]{6,18}[0-9]$")]
    private static partial Regex PhonePattern();

    /// <summary>Lower-case ASCII with accents removed; every other character becomes a space.</summary>
    public static string Fold(string value)
    {
        var builder = new StringBuilder(value.Length);
        foreach (var raw in value.ToLowerInvariant())
        {
            switch (raw)
            {
                case 'œ':
                    builder.Append("oe");
                    break;
                case 'æ':
                    builder.Append("ae");
                    break;
                default:
                    var index = Accented.IndexOf(raw, StringComparison.Ordinal);
                    builder.Append(index >= 0 ? Plain[index] : raw is (>= 'a' and <= 'z') or (>= '0' and <= '9') ? raw : ' ');
                    break;
            }
        }

        return builder.ToString();
    }

    /// <summary>URL slug: "Maison Éclat Coiffure" becomes "maison-eclat-coiffure" (never empty).</summary>
    public static string Slugify(string name)
    {
        var slug = NonAlphanumeric().Replace(Fold(name), "-").Trim('-');
        if (slug.Length > 120)
        {
            slug = slug[..120].Trim('-');
        }

        return slug.Length == 0 ? "etablissement" : slug;
    }

    /// <summary>Order-insensitive, accent-insensitive comparison of two person names.</summary>
    public static bool SameName(string a, string b)
    {
        static string[] Tokens(string value) => Fold(value).Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .OrderBy(t => t, StringComparer.Ordinal).ToArray();

        return Tokens(a).SequenceEqual(Tokens(b), StringComparer.Ordinal);
    }

    /// <summary>Upper-case document number without spaces or separators, used for duplicate detection.</summary>
    public static string NormalizeDocumentNumber(string value) =>
        new(value.Where(char.IsLetterOrDigit).Select(char.ToUpperInvariant).ToArray());

    /// <summary>Returns the phone without spaces/dots/dashes when plausible (8 to 15 digits, optional +), otherwise null.</summary>
    public static string? NormalizePhone(string? value)
    {
        if (string.IsNullOrWhiteSpace(value) || !PhonePattern().IsMatch(value.Trim()))
        {
            return null;
        }

        var cleaned = new string(value.Where(c => char.IsDigit(c) || c == '+').ToArray());
        var digits = cleaned.Count(char.IsDigit);
        return digits is >= 8 and <= 15 ? cleaned : null;
    }

    /// <summary>Indicative checks shown next to the identity file (PRD ADM-03). They never decide on their own.</summary>
    public static IReadOnlyList<KycCheck> EvaluateKyc(KycIdentity identity, string accountName, bool duplicateDocumentNumber, DateOnly today)
    {
        var checks = new List<KycCheck>();

        if (identity.DocumentExpiry is { } expiry)
        {
            checks.Add(expiry < today
                ? new KycCheck("document_expired", "error", $"Document expiré depuis le {expiry:dd/MM/yyyy}.")
                : new KycCheck("document_valid", "ok", $"Document valide jusqu'au {expiry:dd/MM/yyyy}."));
        }
        else
        {
            checks.Add(new KycCheck("expiry_missing", "warning", "Date d'expiration non renseignée."));
        }

        checks.Add(SameName(identity.HolderName, accountName)
            ? new KycCheck("name_match", "ok", "Le nom saisi correspond au nom du compte.")
            : new KycCheck("name_mismatch", "warning", $"Le nom saisi (« {identity.HolderName} ») diffère du nom du compte (« {accountName} »)."));

        checks.Add(duplicateDocumentNumber
            ? new KycCheck("duplicate_document", "error", "Ce numéro de pièce est déjà utilisé par un autre compte.")
            : new KycCheck("document_unique", "ok", "Numéro de pièce jamais vu sur un autre compte."));

        if (!identity.HasRequiredDocuments)
        {
            checks.Add(new KycCheck("documents_missing", "error", "Recto ou verso manquant."));
        }

        return checks;
    }
}
