using System.Security.Cryptography;
using KekeBeauty.Modules.Identity.Domain;
using KekeBeauty.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace KekeBeauty.Modules.Identity.Application;

/// <summary>
/// WhatsApp sign-in: a number is first linked to the account by a code sent to it, then that number plus a fresh code is a first
/// factor, exactly like a password (the authenticator code is still required afterwards). The whole feature is off until
/// <c>Auth:WhatsApp:Enabled</c> is switched on.
/// </summary>
public sealed partial class AuthService
{
    // ───────── Sign-in ─────────

    /// <summary>Sends a sign-in code. The answer is the same whether or not the number belongs to an account, so numbers cannot be probed.</summary>
    public async Task<Result> RequestWhatsAppLoginAsync(string? phone, CancellationToken ct)
    {
        if (!Options.WhatsApp.Enabled)
        {
            return Result.Failure(AuthErrors.ProviderDisabled);
        }

        var e164 = PhoneNumbers.ToE164(phone);
        if (e164 is null)
        {
            return Result.Failure(AuthErrors.InvalidPhone);
        }

        var user = await db.Users.FirstOrDefaultAsync(u => u.PhoneNumber == e164 && u.PhoneVerifiedAt != null && u.Status == UserStatus.Active, ct);
        if (user is not null && await CanSendCodeAsync(e164, PhoneCodePurpose.Login, ct))
        {
            await SendCodeAsync(user.Id, e164, PhoneCodePurpose.Login, ct);
        }

        return Result.Success();
    }

    /// <summary>Checks the sign-in code and, when right, starts the usual second factor.</summary>
    public async Task<Result<ChallengeResponse>> VerifyWhatsAppLoginAsync(WhatsAppVerifyRequest request, CancellationToken ct, string? trustedDeviceToken = null)
    {
        if (!Options.WhatsApp.Enabled)
        {
            return Result.Failure<ChallengeResponse>(AuthErrors.ProviderDisabled);
        }

        var e164 = PhoneNumbers.ToE164(request.Phone);
        if (e164 is null)
        {
            return Result.Failure<ChallengeResponse>(AuthErrors.InvalidCode);
        }

        var code = await CheckCodeAsync(null, e164, PhoneCodePurpose.Login, request.Code, ct);
        if (code is null)
        {
            return Result.Failure<ChallengeResponse>(AuthErrors.InvalidCode);
        }

        var user = await db.Users.FirstOrDefaultAsync(u => u.PhoneNumber == e164 && u.PhoneVerifiedAt != null, ct);
        if (user is null || user.Status != UserStatus.Active)
        {
            return Result.Failure<ChallengeResponse>(AuthErrors.InvalidCode);
        }

        if (user.IsLocked(time.GetUtcNow()))
        {
            return Result.Failure<ChallengeResponse>(AuthErrors.AccountLocked);
        }

        code.Consume(time.GetUtcNow());
        return Result.Success(await StartSecondFactorAsync(user, ct, trustedDeviceToken));
    }

    // ───────── Linking a number to the signed-in account ─────────

    public async Task<WhatsAppLinkStatus> WhatsAppStatusAsync(Guid userId, CancellationToken ct)
    {
        var phone = await db.Users.Where(u => u.Id == userId && u.PhoneVerifiedAt != null).Select(u => u.PhoneNumber).FirstOrDefaultAsync(ct);
        return new WhatsAppLinkStatus(Options.WhatsApp.Enabled, phone is not null, phone is null ? null : PhoneNumbers.Mask(phone));
    }

    public async Task<Result> BeginWhatsAppLinkAsync(Guid userId, string? phone, CancellationToken ct)
    {
        if (!Options.WhatsApp.Enabled)
        {
            return Result.Failure(AuthErrors.ProviderDisabled);
        }

        var e164 = PhoneNumbers.ToE164(phone);
        if (e164 is null)
        {
            return Result.Failure(AuthErrors.InvalidPhone);
        }

        if (await db.Users.AnyAsync(u => u.Id != userId && u.PhoneNumber == e164, ct))
        {
            return Result.Failure(AuthErrors.PhoneTaken);
        }

        if (await CanSendCodeAsync(e164, PhoneCodePurpose.Link, ct))
        {
            await SendCodeAsync(userId, e164, PhoneCodePurpose.Link, ct);
        }

        return Result.Success();
    }

    public async Task<Result> ConfirmWhatsAppLinkAsync(Guid userId, string? codeText, CancellationToken ct)
    {
        if (!Options.WhatsApp.Enabled)
        {
            return Result.Failure(AuthErrors.ProviderDisabled);
        }

        var pending = await db.PhoneCodes
            .Where(c => c.UserId == userId && c.Purpose == PhoneCodePurpose.Link && c.ConsumedAt == null)
            .OrderByDescending(c => c.CreatedAt)
            .FirstOrDefaultAsync(ct);
        if (pending is null)
        {
            return Result.Failure(AuthErrors.InvalidCode);
        }

        var code = await CheckCodeAsync(userId, pending.Phone, PhoneCodePurpose.Link, codeText, ct);
        var user = await db.Users.FirstOrDefaultAsync(u => u.Id == userId, ct);
        if (code is null || user is null)
        {
            return Result.Failure(AuthErrors.InvalidCode);
        }

        if (await db.Users.AnyAsync(u => u.Id != userId && u.PhoneNumber == code.Phone, ct))
        {
            return Result.Failure(AuthErrors.PhoneTaken);
        }

        var now = time.GetUtcNow();
        code.Consume(now);
        user.LinkPhone(code.Phone, now);
        await db.SaveChangesAsync(ct);
        return Result.Success();
    }

    public async Task<Result> UnlinkWhatsAppAsync(Guid userId, CancellationToken ct)
    {
        var user = await db.Users.FirstOrDefaultAsync(u => u.Id == userId, ct);
        if (user is null)
        {
            return Result.Failure(AuthErrors.InvalidInput);
        }

        user.UnlinkPhone();
        await db.SaveChangesAsync(ct);
        return Result.Success();
    }

    // ───────── Codes ─────────

    private async Task<bool> CanSendCodeAsync(string phone, PhoneCodePurpose purpose, CancellationToken ct)
    {
        var since = time.GetUtcNow().AddMinutes(-10);
        return await db.PhoneCodes.CountAsync(c => c.Phone == phone && c.Purpose == purpose && c.CreatedAt > since, ct) < Options.WhatsApp.MaxCodesPer10Minutes;
    }

    private async Task SendCodeAsync(Guid userId, string phone, PhoneCodePurpose purpose, CancellationToken ct)
    {
        var now = time.GetUtcNow();
        var id = Guid.CreateVersion7();
        var digits = RandomNumberGenerator.GetInt32(0, 1_000_000).ToString("D6", System.Globalization.CultureInfo.InvariantCulture);
        db.PhoneCodes.Add(PhoneCode.Create(id, purpose == PhoneCodePurpose.Link ? userId : null, phone, purpose, HashCode(id, digits), now, now.AddMinutes(Options.WhatsApp.CodeMinutes)));
        await db.SaveChangesAsync(ct);

        // A delivery failure is not reported to the caller (it would reveal whether the number is known); the person can ask again.
        try
        {
            await whatsApp.SendTemplateAsync(phone, Options.WhatsApp.OtpTemplate, Options.WhatsApp.Language, [digits], true, ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // Swallowed on purpose, see above.
        }
    }

    /// <summary>The newest unused code for this number and purpose, if the digits match. Wrong digits count against the code.</summary>
    private async Task<PhoneCode?> CheckCodeAsync(Guid? userId, string phone, PhoneCodePurpose purpose, string? digits, CancellationToken ct)
    {
        var now = time.GetUtcNow();
        var query = db.PhoneCodes.Where(c => c.Phone == phone && c.Purpose == purpose && c.ConsumedAt == null);
        if (userId is not null)
        {
            query = query.Where(c => c.UserId == userId);
        }

        var code = await query.OrderByDescending(c => c.CreatedAt).FirstOrDefaultAsync(ct);
        if (code is null || !code.IsUsable(now))
        {
            return null;
        }

        var supplied = (digits ?? string.Empty).Trim();
        var matches = CryptographicOperations.FixedTimeEquals(
            System.Text.Encoding.ASCII.GetBytes(HashCode(code.Id, supplied)),
            System.Text.Encoding.ASCII.GetBytes(code.CodeHash));
        if (!matches)
        {
            code.RegisterFailedAttempt();
            await db.SaveChangesAsync(ct);
            return null;
        }

        return code;
    }

    private static string HashCode(Guid id, string digits) => TokenService.Hash($"{id:N}:{digits}");
}
