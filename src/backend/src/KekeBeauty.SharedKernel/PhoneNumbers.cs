using System.Text.RegularExpressions;

namespace KekeBeauty.SharedKernel;

/// <summary>Phone number helpers. Numbers are stored and exchanged in E.164 form (<c>+2250700000000</c>).</summary>
public static partial class PhoneNumbers
{
    public const string DefaultCountryPrefix = "+225";

    [GeneratedRegex(@"^\+[1-9]\d{7,14}$")]
    private static partial Regex E164();

    /// <summary>
    /// Normalises user input to E.164, or returns null when it is not a plausible number. A number without country code is
    /// read as a local one under <paramref name="defaultPrefix"/> (the leading 0 of ten-digit Ivorian numbers is part of the number).
    /// </summary>
    public static string? ToE164(string? input, string defaultPrefix = DefaultCountryPrefix)
    {
        if (string.IsNullOrWhiteSpace(input))
        {
            return null;
        }

        var trimmed = input.Trim();
        var digits = new string(trimmed.Where(char.IsAsciiDigit).ToArray());
        string candidate;
        if (trimmed.StartsWith('+'))
        {
            candidate = "+" + digits;
        }
        else if (trimmed.StartsWith("00", StringComparison.Ordinal))
        {
            candidate = "+" + digits[2..];
        }
        else if (digits.StartsWith(defaultPrefix.TrimStart('+'), StringComparison.Ordinal) && digits.Length > defaultPrefix.Length + 6)
        {
            candidate = "+" + digits;
        }
        else
        {
            // Ivorian numbers have had ten digits since 2021 and keep their leading 0 after +225 (+225 05 85 74 33 42).
            candidate = defaultPrefix + digits;
        }

        return E164().IsMatch(candidate) ? candidate : null;
    }

    /// <summary>Digits only (no <c>+</c>): the form LeTexto and WhatsApp expect.</summary>
    public static string Digits(string e164) => e164.TrimStart('+');

    /// <summary>Masks all but the last four digits for logs and screens.</summary>
    public static string Mask(string phone) => phone.Length > 4 ? new string('*', phone.Length - 4) + phone[^4..] : phone;
}
