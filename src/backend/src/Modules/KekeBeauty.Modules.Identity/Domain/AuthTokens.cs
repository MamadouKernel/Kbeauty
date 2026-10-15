using KekeBeauty.SharedKernel;

namespace KekeBeauty.Modules.Identity.Domain;

/// <summary>Opaque rotating refresh token. Only its SHA-256 hash is stored; <see cref="SessionId"/> survives rotations.</summary>
public sealed class RefreshToken : Entity
{
    private RefreshToken()
    {
    }

    public Guid UserId { get; private set; }
    public Guid SessionId { get; private set; }
    public string TokenHash { get; private set; } = string.Empty;
    public DateTimeOffset SessionStartedAt { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset ExpiresAt { get; private set; }
    public DateTimeOffset? RevokedAt { get; private set; }
    public Guid? ReplacedById { get; private set; }
    public string? DeviceInfo { get; private set; }
    public string? IpAddress { get; private set; }

    public static RefreshToken Create(Guid userId, Guid sessionId, string tokenHash, DateTimeOffset sessionStartedAt, DateTimeOffset now, DateTimeOffset expiresAt, string? deviceInfo, string? ipAddress) => new()
    {
        UserId = userId,
        SessionId = sessionId,
        TokenHash = tokenHash,
        SessionStartedAt = sessionStartedAt,
        CreatedAt = now,
        ExpiresAt = expiresAt,
        DeviceInfo = deviceInfo,
        IpAddress = ipAddress,
    };

    public bool IsActive(DateTimeOffset now) => RevokedAt is null && ExpiresAt > now;

    public void Revoke(DateTimeOffset now, Guid? replacedById = null)
    {
        RevokedAt ??= now;
        ReplacedById = replacedById;
    }
}

public enum EmailTokenPurpose
{
    ConfirmEmail,
    ResetPassword,
}

public sealed class EmailToken : Entity
{
    private EmailToken()
    {
    }

    public Guid UserId { get; private set; }
    public EmailTokenPurpose Purpose { get; private set; }
    public string TokenHash { get; private set; } = string.Empty;
    public DateTimeOffset ExpiresAt { get; private set; }
    public DateTimeOffset? ConsumedAt { get; private set; }

    public static EmailToken Create(Guid userId, EmailTokenPurpose purpose, string tokenHash, DateTimeOffset expiresAt) => new()
    {
        UserId = userId,
        Purpose = purpose,
        TokenHash = tokenHash,
        ExpiresAt = expiresAt,
    };

    public bool IsUsable(DateTimeOffset now) => ConsumedAt is null && ExpiresAt > now;

    public void Consume(DateTimeOffset now) => ConsumedAt = now;
}

public enum ChallengePurpose
{
    TwoFactor,
    TwoFactorSetup,
}

/// <summary>Short-lived proof that the first factor (password / Google) succeeded; exchanged for a session after 2FA.</summary>
public sealed class LoginChallenge : Entity
{
    public const int MaxAttempts = 5;

    private LoginChallenge()
    {
    }

    public Guid UserId { get; private set; }
    public ChallengePurpose Purpose { get; private set; }
    public string TokenHash { get; private set; } = string.Empty;
    public DateTimeOffset ExpiresAt { get; private set; }
    public int Attempts { get; private set; }
    public DateTimeOffset? ConsumedAt { get; private set; }

    public static LoginChallenge Create(Guid userId, ChallengePurpose purpose, string tokenHash, DateTimeOffset expiresAt) => new()
    {
        UserId = userId,
        Purpose = purpose,
        TokenHash = tokenHash,
        ExpiresAt = expiresAt,
    };

    public bool IsUsable(DateTimeOffset now) => ConsumedAt is null && ExpiresAt > now && Attempts < MaxAttempts;

    public void RegisterFailedAttempt() => Attempts++;

    public void Consume(DateTimeOffset now) => ConsumedAt = now;
}

public enum PhoneCodePurpose
{
    /// <summary>Proves that a signed-in user owns the phone number they want to link.</summary>
    Link,

    /// <summary>First factor of a WhatsApp sign-in.</summary>
    Login,
}

/// <summary>One-time 6-digit code sent by WhatsApp. Only a salted hash is stored; five wrong tries burn the code.</summary>
public sealed class PhoneCode : Entity
{
    public const int MaxAttempts = 5;

    private PhoneCode()
    {
    }

    public Guid? UserId { get; private set; }
    public string Phone { get; private set; } = string.Empty;
    public PhoneCodePurpose Purpose { get; private set; }
    public string CodeHash { get; private set; } = string.Empty;
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset ExpiresAt { get; private set; }
    public int Attempts { get; private set; }
    public DateTimeOffset? ConsumedAt { get; private set; }

    public static PhoneCode Create(Guid id, Guid? userId, string phone, PhoneCodePurpose purpose, string codeHash, DateTimeOffset now, DateTimeOffset expiresAt) => new()
    {
        Id = id,
        UserId = userId,
        Phone = phone,
        Purpose = purpose,
        CodeHash = codeHash,
        CreatedAt = now,
        ExpiresAt = expiresAt,
    };

    public bool IsUsable(DateTimeOffset now) => ConsumedAt is null && ExpiresAt > now && Attempts < MaxAttempts;

    public void RegisterFailedAttempt() => Attempts++;

    public void Consume(DateTimeOffset now) => ConsumedAt = now;
}

public sealed class RecoveryCode : Entity
{
    private RecoveryCode()
    {
    }

    public Guid UserId { get; private set; }
    public string CodeHash { get; private set; } = string.Empty;
    public DateTimeOffset? UsedAt { get; private set; }

    public static RecoveryCode Create(Guid userId, string codeHash) => new() { UserId = userId, CodeHash = codeHash };

    public void MarkUsed(DateTimeOffset now) => UsedAt = now;
}
