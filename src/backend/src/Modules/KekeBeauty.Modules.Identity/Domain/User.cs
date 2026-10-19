using KekeBeauty.SharedKernel;

namespace KekeBeauty.Modules.Identity.Domain;

public static class AppRoles
{
    public const string Client = "Client";
    public const string PartnerOwner = "PartnerOwner";
    public const string PartnerStaff = "PartnerStaff";
    public const string Admin = "Admin";
    public const string SuperAdmin = "SuperAdmin";
    public const string AdminKyc = "AdminKyc";
    public const string AdminSupport = "AdminSupport";
    public const string AdminAccountant = "AdminAccountant";

    /// <summary>Roles a visitor may pick when creating an account (admins are created by a SuperAdmin).</summary>
    public static readonly IReadOnlyList<string> SelfServiceRoles = [Client, PartnerOwner];

    /// <summary>All back-office roles (SuperAdmin, generalist Admin, and specialized roles).</summary>
    public static readonly IReadOnlyList<string> AllAdminRoles = [SuperAdmin, Admin, AdminKyc, AdminSupport, AdminAccountant];

    /// <summary>Admin roles that a SuperAdmin can grant or revoke.</summary>
    public static readonly IReadOnlyList<string> DelegatedAdminRoles = [Admin, AdminKyc, AdminSupport, AdminAccountant];
}

public enum UserStatus
{
    Active,
    Suspended,
}

public sealed class User : Entity
{
    private User()
    {
    }

    /// <summary>Lower-cased, trimmed e-mail (stored as citext, unique).</summary>
    public string Email { get; private set; } = string.Empty;

    public bool EmailConfirmed { get; private set; }
    public string? PasswordHash { get; private set; }
    public string FirstName { get; private set; } = string.Empty;
    public string LastName { get; private set; } = string.Empty;
    public List<string> Roles { get; private set; } = [];
    public UserStatus Status { get; private set; } = UserStatus.Active;
    public int FailedLoginCount { get; private set; }
    public DateTimeOffset? LockedUntil { get; private set; }

    /// <summary>TOTP secret protected with ASP.NET Data Protection. Pending until <see cref="TotpEnabled"/>.</summary>
    public string? TotpSecretProtected { get; private set; }

    public bool TotpEnabled { get; private set; }

    /// <summary>Last accepted TOTP time step; codes at or before it are rejected (replay protection).</summary>
    public long? TotpLastStep { get; private set; }

    public string? GoogleSubject { get; private set; }

    /// <summary>E.164 number linked to the account by a WhatsApp code; used for WhatsApp sign-in and nothing else.</summary>
    public string? PhoneNumber { get; private set; }

    public DateTimeOffset? PhoneVerifiedAt { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }

    /// <summary>When the person accepted the terms and the privacy policy (proof of consent, ARTCI).</summary>
    public DateTimeOffset? TermsAcceptedAt { get; private set; }

    /// <summary>Set when the account was erased on the owner's request; the row remains for referential integrity only.</summary>
    public DateTimeOffset? DeletedAt { get; private set; }

    public static User Register(string email, string firstName, string lastName, string role, bool emailConfirmed, DateTimeOffset now) => new()
    {
        Email = email,
        FirstName = firstName,
        LastName = lastName,
        Roles = [role],
        EmailConfirmed = emailConfirmed,
        CreatedAt = now,
    };

    public void AcceptTerms(DateTimeOffset now) => TermsAcceptedAt = now;

    public void LinkPhone(string e164, DateTimeOffset now)
    {
        PhoneNumber = e164;
        PhoneVerifiedAt = now;
    }

    public void UnlinkPhone()
    {
        PhoneNumber = null;
        PhoneVerifiedAt = null;
    }

    /// <summary>Right to erasure: nothing personal survives, the account can no longer sign in.</summary>
    public void Anonymize(DateTimeOffset now)
    {
        Email = $"deleted-{Id:N}@deleted.invalid";
        FirstName = "Compte";
        LastName = "supprimé";
        EmailConfirmed = false;
        PasswordHash = null;
        TotpSecretProtected = null;
        TotpEnabled = false;
        TotpLastStep = null;
        GoogleSubject = null;
        PhoneNumber = null;
        PhoneVerifiedAt = null;
        FailedLoginCount = 0;
        LockedUntil = null;
        Status = UserStatus.Suspended;
        DeletedAt = now;
    }

    public bool IsLocked(DateTimeOffset now) => LockedUntil is { } until && until > now;

    public void SetPassword(string passwordHash)
    {
        PasswordHash = passwordHash;
        FailedLoginCount = 0;
        LockedUntil = null;
    }

    public void ConfirmEmail() => EmailConfirmed = true;

    public void RegisterFailedLogin(DateTimeOffset now, int maxAttempts, TimeSpan lockout)
    {
        FailedLoginCount++;
        if (FailedLoginCount >= maxAttempts)
        {
            LockedUntil = now + lockout;
            FailedLoginCount = 0;
        }
    }

    public void ResetFailedLogins()
    {
        FailedLoginCount = 0;
        LockedUntil = null;
    }

    public void UpdateProfile(string firstName, string lastName)
    {
        FirstName = firstName;
        LastName = lastName;
    }

    public void BeginTotp(string protectedSecret)
    {
        TotpSecretProtected = protectedSecret;
        TotpEnabled = false;
        TotpLastStep = null;
    }

    public void EnableTotp(long step)
    {
        TotpEnabled = true;
        TotpLastStep = step;
    }

    public void RecordTotpStep(long step) => TotpLastStep = step;

    public void LinkGoogle(string subject) => GoogleSubject = subject;

    public void Suspend() => Status = UserStatus.Suspended;

    public void Reactivate() => Status = UserStatus.Active;

    /// <summary>Replaces the whole list so that change tracking sees the new value.</summary>
    public void SetRoles(IEnumerable<string> roles) => Roles = roles.Distinct().ToList();
}
