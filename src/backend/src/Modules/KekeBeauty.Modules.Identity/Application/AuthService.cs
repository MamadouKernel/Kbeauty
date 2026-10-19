using KekeBeauty.Modules.Identity.Domain;
using KekeBeauty.SharedKernel;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace KekeBeauty.Modules.Identity.Application;

/// <summary>
/// Authentication use cases. First factors (password, Google) only ever yield a <see cref="LoginChallenge"/>;
/// a session is issued exclusively after a valid TOTP / recovery code.
/// </summary>
public sealed partial class AuthService(
    IIdentityDbContext db,
    IPasswordHasher<User> hasher,
    TokenService tokens,
    TotpService totp,
    IEmailSender email,
    IGoogleIdTokenVerifier google,
    ISessionPolicy sessionPolicy,
    IWhatsAppSender whatsApp,
    IOptions<AuthOptions> authOptions,
    TimeProvider time)
{
    private static readonly User DummyUser = User.Register("dummy@invalid.local", "-", "-", AppRoles.Client, false, DateTimeOffset.UnixEpoch);

    private AuthOptions Options => authOptions.Value;

    // ───────── Registration, confirmation, password reset ─────────

    public async Task<Result> RegisterAsync(RegisterRequest request, CancellationToken ct)
    {
        var emailAddress = NormalizeEmail(request.Email);
        if (!PasswordRules.IsValidEmail(emailAddress))
        {
            return Result.Failure(AuthErrors.InvalidEmail);
        }

        if (!AppRoles.SelfServiceRoles.Contains(request.Role))
        {
            return Result.Failure(AuthErrors.InvalidRole);
        }

        if (!request.AcceptTerms)
        {
            return Result.Failure(AuthErrors.TermsRequired);
        }

        var firstName = request.FirstName?.Trim() ?? string.Empty;
        var lastName = request.LastName?.Trim() ?? string.Empty;
        if (firstName.Length is 0 or > 100 || lastName.Length is 0 or > 100)
        {
            return Result.Failure(AuthErrors.InvalidInput);
        }

        var passwordErrors = PasswordRules.Validate(request.Password, emailAddress);
        if (passwordErrors.Count > 0)
        {
            return Result.Failure(AuthErrors.WeakPassword(string.Join(' ', passwordErrors)));
        }

        var existing = await db.Users.AnyAsync(u => u.Email == emailAddress, ct);
        if (existing)
        {
            // Same answer as a fresh registration: never reveal whether an account exists.
            await email.SendAsync(
                new EmailMessage(
                    emailAddress,
                    "Vous avez déjà un compte Keke Beauty",
                    $"Quelqu'un a tenté de créer un compte avec cette adresse, mais un compte existe déjà.\n\nMot de passe oublié ? {WebUrl("mot-de-passe-oublie")}\n\nSi ce n'était pas vous, ignorez ce message."),
                ct);
            return Result.Success();
        }

        var now = time.GetUtcNow();
        var user = User.Register(emailAddress, firstName, lastName, request.Role, emailConfirmed: false, now);
        user.AcceptTerms(now);
        user.SetPassword(hasher.HashPassword(user, request.Password));
        db.Users.Add(user);
        var token = IssueEmailToken(user, EmailTokenPurpose.ConfirmEmail, now.AddHours(Options.ConfirmEmailHours));

        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException)
        {
            // Lost a race with a concurrent registration of the same e-mail: behave as "already registered".
            return Result.Success();
        }

        await email.SendAsync(
            new EmailMessage(
                emailAddress,
                "Confirmez votre adresse e-mail — Keke Beauty",
                $"Bonjour {firstName},\n\nBienvenue sur Keke Beauty. Confirmez votre adresse e-mail (lien valable {Options.ConfirmEmailHours} h) :\n{WebUrl("confirmation-email", token)}\n\nSi vous n'êtes pas à l'origine de cette inscription, ignorez ce message."),
            ct);
        return Result.Success();
    }

    public async Task<Result> ConfirmEmailAsync(string? token, CancellationToken ct)
    {
        var emailToken = await FindEmailTokenAsync(token, EmailTokenPurpose.ConfirmEmail, ct);
        if (emailToken is null)
        {
            return Result.Failure(AuthErrors.InvalidToken);
        }

        var user = await db.Users.FirstAsync(u => u.Id == emailToken.UserId, ct);
        user.ConfirmEmail();
        emailToken.Consume(time.GetUtcNow());
        await db.SaveChangesAsync(ct);
        return Result.Success();
    }

    public async Task<Result> ForgotPasswordAsync(string? emailAddress, CancellationToken ct)
    {
        var normalized = NormalizeEmail(emailAddress);
        var user = await db.Users.FirstOrDefaultAsync(u => u.Email == normalized, ct);
        if (user is null || user.Status != UserStatus.Active)
        {
            return Result.Success(); // anti-enumeration
        }

        var token = IssueEmailToken(user, EmailTokenPurpose.ResetPassword, time.GetUtcNow().AddMinutes(Options.ResetPasswordMinutes));
        await db.SaveChangesAsync(ct);
        await email.SendAsync(
            new EmailMessage(
                user.Email,
                "Réinitialisation de votre mot de passe — Keke Beauty",
                $"Bonjour {user.FirstName},\n\nPour choisir un nouveau mot de passe (lien valable {Options.ResetPasswordMinutes} min, usage unique) :\n{WebUrl("reinitialiser-mot-de-passe", token)}\n\nSi vous n'avez rien demandé, ignorez ce message : votre mot de passe reste inchangé."),
            ct);
        return Result.Success();
    }

    public async Task<Result> ResetPasswordAsync(ResetPasswordRequest request, CancellationToken ct)
    {
        var emailToken = await FindEmailTokenAsync(request.Token, EmailTokenPurpose.ResetPassword, ct);
        if (emailToken is null)
        {
            return Result.Failure(AuthErrors.InvalidToken);
        }

        var user = await db.Users.FirstAsync(u => u.Id == emailToken.UserId, ct);
        var passwordErrors = PasswordRules.Validate(request.NewPassword, user.Email);
        if (passwordErrors.Count > 0)
        {
            return Result.Failure(AuthErrors.WeakPassword(string.Join(' ', passwordErrors)));
        }

        var now = time.GetUtcNow();
        user.SetPassword(hasher.HashPassword(user, request.NewPassword));
        user.ConfirmEmail(); // the reset link proves control of the mailbox
        emailToken.Consume(now);
        await RevokeAllSessionsAsync(user.Id, now, ct);
        await db.SaveChangesAsync(ct);
        return Result.Success();
    }

    // ───────── First factors ─────────

    public async Task<Result<ChallengeResponse>> LoginAsync(LoginRequest request, CancellationToken ct, string? trustedDeviceToken = null)
    {
        var normalized = NormalizeEmail(request.Email);
        var user = await db.Users.FirstOrDefaultAsync(u => u.Email == normalized, ct);
        var now = time.GetUtcNow();

        if (user?.PasswordHash is null)
        {
            hasher.HashPassword(DummyUser, request.Password ?? string.Empty); // equalise timing with a real check
            return Result.Failure<ChallengeResponse>(AuthErrors.InvalidCredentials);
        }

        if (user.IsLocked(now))
        {
            return Result.Failure<ChallengeResponse>(AuthErrors.AccountLocked);
        }

        var verification = hasher.VerifyHashedPassword(user, user.PasswordHash, request.Password ?? string.Empty);
        if (verification == PasswordVerificationResult.Failed)
        {
            user.RegisterFailedLogin(now, Options.MaxFailedLogins, TimeSpan.FromMinutes(Options.LockoutMinutes));
            await db.SaveChangesAsync(ct);
            return Result.Failure<ChallengeResponse>(AuthErrors.InvalidCredentials);
        }

        if (user.Status != UserStatus.Active)
        {
            return Result.Failure<ChallengeResponse>(AuthErrors.AccountSuspended);
        }

        if (!user.EmailConfirmed)
        {
            return Result.Failure<ChallengeResponse>(AuthErrors.EmailNotConfirmed);
        }

        if (verification == PasswordVerificationResult.SuccessRehashNeeded)
        {
            user.SetPassword(hasher.HashPassword(user, request.Password!));
        }

        return Result.Success(await StartSecondFactorAsync(user, ct, trustedDeviceToken));
    }

    public async Task<Result<ChallengeResponse>> LoginWithGoogleAsync(string? idToken, CancellationToken ct, string? trustedDeviceToken = null)
    {
        if (!Options.Google.Enabled)
        {
            return Result.Failure<ChallengeResponse>(AuthErrors.ProviderDisabled);
        }

        var identity = string.IsNullOrWhiteSpace(idToken) ? null : await google.VerifyAsync(idToken, ct);
        if (identity is null || !identity.EmailVerified || !PasswordRules.IsValidEmail(NormalizeEmail(identity.Email)))
        {
            return Result.Failure<ChallengeResponse>(AuthErrors.InvalidCredentials);
        }

        var emailAddress = NormalizeEmail(identity.Email);
        var user = await db.Users.FirstOrDefaultAsync(u => u.GoogleSubject == identity.Subject, ct)
            ?? await db.Users.FirstOrDefaultAsync(u => u.Email == emailAddress, ct);

        if (user is null)
        {
            user = User.Register(emailAddress, Fallback(identity.FirstName), Fallback(identity.LastName), AppRoles.Client, emailConfirmed: true, time.GetUtcNow());
            db.Users.Add(user);
        }

        if (user.GoogleSubject is null)
        {
            user.LinkGoogle(identity.Subject);
        }

        if (user.Status != UserStatus.Active)
        {
            return Result.Failure<ChallengeResponse>(AuthErrors.AccountSuspended);
        }

        user.ConfirmEmail(); // Google asserted the address is verified
        return Result.Success(await StartSecondFactorAsync(user, ct, trustedDeviceToken));
    }

    // ───────── Second factor (Google Authenticator) ─────────

    public async Task<Result<TwoFactorSetupResponse>> BeginTwoFactorSetupAsync(string? challengeToken, CancellationToken ct)
    {
        var loaded = await LoadChallengeAsync(challengeToken, ChallengePurpose.TwoFactorSetup, ct);
        if (loaded is null)
        {
            return Result.Failure<TwoFactorSetupResponse>(AuthErrors.ChallengeExpired);
        }

        var secret = totp.GenerateSecret();
        loaded.Value.User.BeginTotp(totp.Protect(secret));
        await db.SaveChangesAsync(ct);
        return Result.Success(new TwoFactorSetupResponse(secret, totp.BuildOtpauthUri(loaded.Value.User.Email, secret)));
    }

    public async Task<Result<IssuedSession>> EnableTwoFactorAsync(ChallengeCodeRequest request, ClientContext context, CancellationToken ct)
    {
        var loaded = await LoadChallengeAsync(request.ChallengeToken, ChallengePurpose.TwoFactorSetup, ct);
        if (loaded is null)
        {
            return Result.Failure<IssuedSession>(AuthErrors.ChallengeExpired);
        }

        var (challenge, user) = loaded.Value;
        if (user.TotpSecretProtected is null)
        {
            return Result.Failure<IssuedSession>(AuthErrors.InvalidInput);
        }

        var code = (request.Code ?? string.Empty).Replace(" ", string.Empty, StringComparison.Ordinal);
        if (!totp.TryVerify(totp.Unprotect(user.TotpSecretProtected), code, null, out var step))
        {
            challenge.RegisterFailedAttempt();
            if (challenge.Attempts >= LoginChallenge.MaxAttempts)
            {
                user.RegisterFailedLogin(time.GetUtcNow(), Options.MaxFailedLogins, TimeSpan.FromMinutes(Options.LockoutMinutes));
            }

            await db.SaveChangesAsync(ct);
            return Result.Failure<IssuedSession>(AuthErrors.InvalidCode);
        }

        user.EnableTotp(step);
        db.RecoveryCodes.RemoveRange(await db.RecoveryCodes.Where(r => r.UserId == user.Id).ToListAsync(ct));
        var recoveryCodes = TokenService.NewRecoveryCodes();
        foreach (var recoveryCode in recoveryCodes)
        {
            db.RecoveryCodes.Add(RecoveryCode.Create(user.Id, TokenService.Hash(TokenService.NormalizeRecoveryCode(recoveryCode))));
        }

        challenge.Consume(time.GetUtcNow());
        return Result.Success(await IssueSessionAsync(user, context, recoveryCodes, request.RememberDevice, ct));
    }

    public async Task<Result<IssuedSession>> VerifyTwoFactorAsync(ChallengeCodeRequest request, ClientContext context, CancellationToken ct)
    {
        var loaded = await LoadChallengeAsync(request.ChallengeToken, ChallengePurpose.TwoFactor, ct);
        if (loaded is null)
        {
            return Result.Failure<IssuedSession>(AuthErrors.ChallengeExpired);
        }

        var (challenge, user) = loaded.Value;
        var now = time.GetUtcNow();
        var trimmed = (request.Code ?? string.Empty).Trim();
        var compact = trimmed.Replace(" ", string.Empty, StringComparison.Ordinal);
        var accepted = false;

        if (user.TotpSecretProtected is not null && compact.Length == 6 && compact.All(char.IsAsciiDigit))
        {
            if (totp.TryVerify(totp.Unprotect(user.TotpSecretProtected), compact, user.TotpLastStep, out var step))
            {
                user.RecordTotpStep(step);
                accepted = true;
            }
        }
        else if (trimmed.Length > 0)
        {
            var hash = TokenService.Hash(TokenService.NormalizeRecoveryCode(trimmed));
            var recoveryCode = await db.RecoveryCodes.FirstOrDefaultAsync(r => r.UserId == user.Id && r.CodeHash == hash && r.UsedAt == null, ct);
            if (recoveryCode is not null)
            {
                recoveryCode.MarkUsed(now);
                accepted = true;
            }
        }

        if (!accepted)
        {
            challenge.RegisterFailedAttempt();
            if (challenge.Attempts >= LoginChallenge.MaxAttempts)
            {
                user.RegisterFailedLogin(now, Options.MaxFailedLogins, TimeSpan.FromMinutes(Options.LockoutMinutes));
            }

            await db.SaveChangesAsync(ct);
            return Result.Failure<IssuedSession>(AuthErrors.InvalidCode);
        }

        challenge.Consume(now);
        return Result.Success(await IssueSessionAsync(user, context, null, request.RememberDevice, ct));
    }

    public async Task<Result<IssuedSession>> CompleteTrustedDeviceAsync(
        string? challengeToken,
        string? trustedDeviceToken,
        ClientContext context,
        CancellationToken ct)
    {
        var loaded = await LoadChallengeAsync(challengeToken, ChallengePurpose.TrustedDevice, ct);
        if (loaded is null || string.IsNullOrWhiteSpace(trustedDeviceToken))
        {
            return Result.Failure<IssuedSession>(AuthErrors.ChallengeExpired);
        }

        var (challenge, user) = loaded.Value;
        var now = time.GetUtcNow();
        var tokenHash = TokenService.Hash(trustedDeviceToken);
        var device = await db.TrustedDevices.FirstOrDefaultAsync(
            d => d.UserId == user.Id && d.TokenHash == tokenHash && d.RevokedAt == null && d.ExpiresAt > now,
            ct);
        if (device is null)
        {
            return Result.Failure<IssuedSession>(AuthErrors.ChallengeExpired);
        }

        device.Touch(now, context.IpAddress);
        challenge.Consume(now);
        return Result.Success(await IssueSessionAsync(user, context, null, rememberDevice: false, ct));
    }

    // ───────── Sessions ─────────

    public async Task<Result<IssuedSession>> RefreshAsync(string? refreshToken, ClientContext context, CancellationToken ct)
    {
        if (string.IsNullOrEmpty(refreshToken))
        {
            return Result.Failure<IssuedSession>(AuthErrors.InvalidToken);
        }

        var hash = TokenService.Hash(refreshToken);
        var current = await db.RefreshTokens.FirstOrDefaultAsync(t => t.TokenHash == hash, ct);
        if (current is null)
        {
            return Result.Failure<IssuedSession>(AuthErrors.InvalidToken);
        }

        var now = time.GetUtcNow();
        if (current.RevokedAt is not null)
        {
            // A rotated/revoked token came back: assume it leaked and end every session of this user.
            await RevokeAllSessionsAsync(current.UserId, now, ct);
            await db.SaveChangesAsync(ct);
            return Result.Failure<IssuedSession>(AuthErrors.InvalidToken);
        }

        var user = await db.Users.FirstOrDefaultAsync(u => u.Id == current.UserId, ct);
        if (current.ExpiresAt <= now || user is null || user.Status != UserStatus.Active)
        {
            return Result.Failure<IssuedSession>(AuthErrors.InvalidToken);
        }

        var raw = TokenService.NewOpaqueToken();
        var idle = await sessionPolicy.IdleTimeoutAsync(ct);
        var expires = SessionExpiry(now, current.SessionStartedAt, idle);
        var next = RefreshToken.Create(user.Id, current.SessionId, TokenService.Hash(raw), current.SessionStartedAt, now, expires, current.DeviceInfo, context.IpAddress ?? current.IpAddress);
        db.RefreshTokens.Add(next);
        current.Revoke(now, next.Id);
        await db.SaveChangesAsync(ct);
        return Result.Success(BuildIssued(user, current.SessionId, raw, expires, null, idle, null, null));
    }

    public async Task LogoutAsync(string? refreshToken, CancellationToken ct, Guid? sessionId = null)
    {
        var now = time.GetUtcNow();
        var changed = false;

        if (!string.IsNullOrEmpty(refreshToken))
        {
            var hash = TokenService.Hash(refreshToken);
            var current = await db.RefreshTokens.FirstOrDefaultAsync(t => t.TokenHash == hash, ct);
            if (current is not null && current.RevokedAt is null)
            {
                current.Revoke(now);
                changed = true;
            }
        }

        if (sessionId is { } sid)
        {
            var sessionTokens = await db.RefreshTokens.Where(t => t.SessionId == sid && t.RevokedAt == null).ToListAsync(ct);
            foreach (var token in sessionTokens)
            {
                token.Revoke(now);
                changed = true;
            }
        }

        if (changed)
        {
            await db.SaveChangesAsync(ct);
        }
    }

    public static UserDto ToDto(User user) =>
        new(user.Id, user.Email, user.FirstName, user.LastName, user.Roles, user.TotpEnabled);

    // ───────── Helpers ─────────

    private static string NormalizeEmail(string? value) => (value ?? string.Empty).Trim().ToLowerInvariant();

    private static string Fallback(string? value) => string.IsNullOrWhiteSpace(value) ? "-" : value.Trim();

    private static string? Truncate(string? value, int max) => value is { Length: > 0 } ? value[..Math.Min(value.Length, max)] : null;

    private string WebUrl(string path, string? token = null) =>
        $"{Options.WebBaseUrl.TrimEnd('/')}/{path}" + (token is null ? string.Empty : $"?token={Uri.EscapeDataString(token)}");

    private string IssueEmailToken(User user, EmailTokenPurpose purpose, DateTimeOffset expiresAt)
    {
        var raw = TokenService.NewOpaqueToken();
        db.EmailTokens.Add(EmailToken.Create(user.Id, purpose, TokenService.Hash(raw), expiresAt));
        return raw;
    }

    private async Task<EmailToken?> FindEmailTokenAsync(string? token, EmailTokenPurpose purpose, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(token))
        {
            return null;
        }

        var hash = TokenService.Hash(token);
        var emailToken = await db.EmailTokens.FirstOrDefaultAsync(t => t.TokenHash == hash && t.Purpose == purpose, ct);
        return emailToken is not null && emailToken.IsUsable(time.GetUtcNow()) ? emailToken : null;
    }

    private async Task<ChallengeResponse> StartSecondFactorAsync(User user, CancellationToken ct, string? trustedDeviceToken = null)
    {
        var now = time.GetUtcNow();
        if (user.TotpEnabled && !string.IsNullOrWhiteSpace(trustedDeviceToken))
        {
            var tokenHash = TokenService.Hash(trustedDeviceToken);
            var isTrusted = await db.TrustedDevices.AsNoTracking()
                .AnyAsync(d => d.UserId == user.Id && d.TokenHash == tokenHash && d.RevokedAt == null && d.ExpiresAt > now, ct);
            if (isTrusted)
            {
                var rawTrusted = TokenService.NewOpaqueToken();
                db.LoginChallenges.Add(LoginChallenge.Create(user.Id, ChallengePurpose.TrustedDevice, TokenService.Hash(rawTrusted), now.AddMinutes(Options.ChallengeMinutes)));
                await db.SaveChangesAsync(ct);
                return new ChallengeResponse(ChallengeStatus.TrustedDevice, rawTrusted);
            }
        }

        var purpose = user.TotpEnabled ? ChallengePurpose.TwoFactor : ChallengePurpose.TwoFactorSetup;
        var raw = TokenService.NewOpaqueToken();
        db.LoginChallenges.Add(LoginChallenge.Create(user.Id, purpose, TokenService.Hash(raw), now.AddMinutes(Options.ChallengeMinutes)));
        await db.SaveChangesAsync(ct);
        return new ChallengeResponse(user.TotpEnabled ? ChallengeStatus.TwoFactorRequired : ChallengeStatus.TwoFactorSetupRequired, raw);
    }

    private async Task<(LoginChallenge Challenge, User User)?> LoadChallengeAsync(string? rawToken, ChallengePurpose purpose, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(rawToken))
        {
            return null;
        }

        var hash = TokenService.Hash(rawToken);
        var challenge = await db.LoginChallenges.FirstOrDefaultAsync(c => c.TokenHash == hash && c.Purpose == purpose, ct);
        if (challenge is null || !challenge.IsUsable(time.GetUtcNow()))
        {
            return null;
        }

        var user = await db.Users.FirstOrDefaultAsync(u => u.Id == challenge.UserId, ct);
        return user is { Status: UserStatus.Active } ? (challenge, user) : null;
    }

    private async Task<IssuedSession> IssueSessionAsync(
        User user,
        ClientContext context,
        IReadOnlyList<string>? recoveryCodes,
        bool rememberDevice,
        CancellationToken ct)
    {
        var now = time.GetUtcNow();
        user.ResetFailedLogins();
        var sessionId = Guid.CreateVersion7();
        var raw = TokenService.NewOpaqueToken();
        var idle = await sessionPolicy.IdleTimeoutAsync(ct);
        var expires = SessionExpiry(now, now, idle);
        db.RefreshTokens.Add(RefreshToken.Create(user.Id, sessionId, TokenService.Hash(raw), now, now, expires, Truncate(context.UserAgent, 256), context.IpAddress));

        string? trustedRaw = null;
        DateTimeOffset? trustedExpires = null;
        if (rememberDevice && Options.TrustedDeviceDays > 0)
        {
            trustedRaw = TokenService.NewOpaqueToken();
            trustedExpires = now.AddDays(Options.TrustedDeviceDays);
            db.TrustedDevices.Add(TrustedDevice.Create(
                user.Id,
                TokenService.Hash(trustedRaw),
                now,
                trustedExpires.Value,
                Truncate(context.UserAgent, 256),
                context.IpAddress));
        }

        await db.SaveChangesAsync(ct);
        return BuildIssued(user, sessionId, raw, expires, recoveryCodes, idle, trustedRaw, trustedExpires);
    }

    /// <summary>A session ends after the configured inactivity (sliding) and never outlives the absolute limit.</summary>
    private DateTimeOffset SessionExpiry(DateTimeOffset now, DateTimeOffset sessionStartedAt, TimeSpan idle)
    {
        var sliding = now.Add(idle);
        var absolute = sessionStartedAt.AddDays(Options.RefreshTokenDays);
        return sliding < absolute ? sliding : absolute;
    }

    private IssuedSession BuildIssued(
        User user,
        Guid sessionId,
        string rawRefreshToken,
        DateTimeOffset refreshExpires,
        IReadOnlyList<string>? recoveryCodes,
        TimeSpan idle,
        string? trustedDeviceToken,
        DateTimeOffset? trustedDeviceExpires)
    {
        var (access, expiresAt) = tokens.CreateAccessToken(user, sessionId);
        return new IssuedSession(
            new AuthResponse(access, expiresAt, ToDto(user), recoveryCodes, (int)idle.TotalMinutes),
            rawRefreshToken,
            refreshExpires,
            trustedDeviceToken,
            trustedDeviceExpires);
    }

    private async Task RevokeAllSessionsAsync(Guid userId, DateTimeOffset now, CancellationToken ct)
    {
        var active = await db.RefreshTokens.Where(t => t.UserId == userId && t.RevokedAt == null).ToListAsync(ct);
        foreach (var token in active)
        {
            token.Revoke(now);
        }

        var devices = await db.TrustedDevices.Where(d => d.UserId == userId && d.RevokedAt == null).ToListAsync(ct);
        foreach (var device in devices)
        {
            device.Revoke(now);
        }
    }
}
