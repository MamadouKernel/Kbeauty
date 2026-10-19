using KekeBeauty.SharedKernel;

namespace KekeBeauty.Modules.Identity.Application;

// ───────── Requests ─────────
public sealed record RegisterRequest(string Email, string Password, string FirstName, string LastName, string Role, bool AcceptTerms = false);

public sealed record ConfirmEmailRequest(string Token);

public sealed record ForgotPasswordRequest(string Email);

public sealed record ResetPasswordRequest(string Token, string NewPassword);

public sealed record LoginRequest(string Email, string Password);

public sealed record GoogleLoginRequest(string IdToken);

public sealed record WhatsAppRequest(string Phone);

public sealed record WhatsAppVerifyRequest(string Phone, string Code);

public sealed record PhoneCodeRequest(string Code);

public sealed record WhatsAppLinkStatus(bool Available, bool Linked, string? PhoneMasked);

public sealed record ChallengeRequest(string ChallengeToken);

public sealed record ChallengeCodeRequest(string ChallengeToken, string Code, bool RememberDevice = false);

public sealed record UpdateProfileRequest(string FirstName, string LastName);

// ───────── Responses ─────────
public static class ChallengeStatus
{
    public const string TwoFactorRequired = "TwoFactorRequired";
    public const string TwoFactorSetupRequired = "TwoFactorSetupRequired";
    public const string TrustedDevice = "TrustedDevice";

    /// <summary>No second factor needed (a plain client): the browser exchanges the challenge for a session right away.</summary>
    public const string PasswordOnly = "PasswordOnly";
}

public sealed record ChallengeResponse(string Status, string ChallengeToken);

public sealed record TwoFactorSetupResponse(string Secret, string OtpauthUri);

public sealed record UserDto(Guid Id, string Email, string FirstName, string LastName, IReadOnlyList<string> Roles, bool TwoFactorEnabled);

/// <summary>Body returned to the browser. The refresh token never appears here: it travels in an httpOnly cookie.</summary>
/// <summary><paramref name="IdleMinutes"/> is the inactivity after which the session ends (admin setting); the web app also signs out on its own.</summary>
public sealed record AuthResponse(string AccessToken, DateTimeOffset ExpiresAt, UserDto User, IReadOnlyList<string>? RecoveryCodes, int IdleMinutes = 60);

public sealed record IssuedSession(
    AuthResponse Response,
    string RefreshToken,
    DateTimeOffset RefreshExpiresAt,
    string? TrustedDeviceToken = null,
    DateTimeOffset? TrustedDeviceExpiresAt = null);

public sealed record ProviderStatus(bool Enabled, string? ClientId);

public sealed record ProvidersResponse(bool Password, ProviderStatus Google, ProviderStatus WhatsApp);

public sealed record SessionDto(Guid Id, DateTimeOffset StartedAt, string? DeviceInfo, string? IpAddress, bool IsCurrent);

public sealed record TrustedDeviceDto(
    Guid Id,
    DateTimeOffset CreatedAt,
    DateTimeOffset LastUsedAt,
    DateTimeOffset ExpiresAt,
    string? DeviceInfo,
    string? IpAddress,
    bool IsCurrent);

public sealed record ClientContext(string? IpAddress, string? UserAgent);

// ───────── Errors (codes are stable; messages are shown to users) ─────────
public static class AuthErrors
{
    public static readonly Error InvalidInput = new("auth.invalid_input", "Les informations saisies sont invalides.");
    public static readonly Error InvalidEmail = new("auth.invalid_email", "Adresse e-mail invalide.");
    public static readonly Error TermsRequired = new("auth.terms_required", "Vous devez accepter les conditions d'utilisation et la politique de confidentialité.");
    public static readonly Error InvalidRole = new("auth.invalid_role", "Type de compte invalide.");
    public static readonly Error InvalidCredentials = new("auth.invalid_credentials", "E-mail ou mot de passe incorrect.");
    public static readonly Error EmailNotConfirmed = new("auth.email_not_confirmed", "Confirmez votre adresse e-mail avant de vous connecter.");
    public static readonly Error AccountLocked = new("auth.account_locked", "Compte temporairement verrouillé après trop d'échecs. Réessayez plus tard.");
    public static readonly Error AccountSuspended = new("auth.account_suspended", "Ce compte est suspendu.");
    public static readonly Error InvalidToken = new("auth.invalid_token", "Lien invalide ou expiré.");
    public static readonly Error ChallengeExpired = new("auth.challenge_expired", "Votre tentative de connexion a expiré. Reconnectez-vous.");
    public static readonly Error InvalidCode = new("auth.invalid_code", "Code invalide.");
    public static readonly Error InvalidPhone = new("auth.invalid_phone", "Numéro de téléphone invalide.");
    public static readonly Error PhoneTaken = new("auth.phone_taken", "Ce numéro ne peut pas être associé à votre compte.");
    public static readonly Error ProviderDisabled = new("auth.provider_disabled", "Ce mode de connexion n'est pas disponible.");

    public static Error WeakPassword(string reasons) => new("auth.weak_password", reasons);
}
