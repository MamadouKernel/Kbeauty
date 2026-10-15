namespace KekeBeauty.Modules.Identity.Application;

public sealed class JwtOptions
{
    public const string SectionName = "Jwt";

    public string Issuer { get; init; } = "keke-beauty";
    public string Audience { get; init; } = "keke-beauty-web";

    /// <summary>HMAC key, at least 32 characters. Never committed: provided by environment/secret manager.</summary>
    public string SigningKey { get; init; } = string.Empty;

    public int AccessTokenMinutes { get; init; } = 15;
}

public sealed class GoogleAuthOptions
{
    /// <summary>Disabled until a Google OAuth client ID is configured.</summary>
    public bool Enabled { get; init; }

    public string ClientId { get; init; } = string.Empty;
}

public sealed class WhatsAppAuthOptions
{
    /// <summary>WhatsApp sign-in (a one-time code sent to a linked number, then the usual authenticator code). Off until switched on.</summary>
    public bool Enabled { get; init; }

    /// <summary>Approved WhatsApp authentication template: one body parameter (the code) and a copy-code button.</summary>
    public string OtpTemplate { get; init; } = "keke_otp";

    public string Language { get; init; } = "fr";
    public int CodeMinutes { get; init; } = 10;

    /// <summary>At most this many codes per number in ten minutes: a ceiling on WhatsApp costs and on harassment of a victim's phone.</summary>
    public int MaxCodesPer10Minutes { get; init; } = 3;
}

public sealed class AuthOptions
{
    public const string SectionName = "Auth";
    public const string RefreshCookieName = "kb_rt";

    public int RefreshTokenDays { get; init; } = 30;
    public int MaxFailedLogins { get; init; } = 5;
    public int LockoutMinutes { get; init; } = 15;
    public int ChallengeMinutes { get; init; } = 10;
    public int ConfirmEmailHours { get; init; } = 24;
    public int ResetPasswordMinutes { get; init; } = 60;
    public string WebBaseUrl { get; init; } = "http://localhost:4200";
    public string TotpIssuer { get; init; } = "Keke Beauty";
    public int AuthPermitPerMinute { get; init; } = 30;

    public GoogleAuthOptions Google { get; init; } = new();
    public WhatsAppAuthOptions WhatsApp { get; init; } = new();
}
