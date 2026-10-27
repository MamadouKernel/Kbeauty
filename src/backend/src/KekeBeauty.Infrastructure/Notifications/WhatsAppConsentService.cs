using KekeBeauty.Infrastructure.Persistence;
using KekeBeauty.Modules.Notifications.Domain;
using KekeBeauty.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace KekeBeauty.Infrastructure.Notifications;

/// <summary>The wording shown next to each box. Changing a text means a new <see cref="Version"/>: agreements to the old one stay valid as proof but a new agreement needs the new text.</summary>
public static class WhatsAppConsentTexts
{
    public const string Version = "2026-10-27";

    public const string Service =
        "J'accepte de recevoir sur WhatsApp, à ce numéro, les confirmations, rappels et informations sur mes rendez-vous Keke Beauty. Je peux me désabonner à tout moment en répondant STOP ou depuis « Mes notifications ».";

    public const string Marketing =
        "J'accepte de recevoir sur WhatsApp, à ce numéro, des invitations et des offres de Keke Beauty (par exemple pour reprendre rendez-vous). Je peux me désabonner à tout moment en répondant STOP ou depuis « Mes notifications ».";

    public static string For(ConsentPurpose purpose) => purpose == ConsentPurpose.Marketing ? Marketing : Service;
}

public sealed record ConsentState(bool Granted, DateTimeOffset? At);

public sealed record WhatsAppConsentStatus(bool Linked, string? PhoneMasked, ConsentState Service, ConsentState Marketing, string TextVersion, string ServiceText, string MarketingText);

public sealed record WhatsAppConsentInput(string? Purpose, bool Granted, string? TextVersion);

/// <summary>
/// The WhatsApp consent register: records agreements and withdrawals as an append-only history and answers "may we write to this number".
/// An agreement only counts for the number that is linked to the account <b>now</b> and only if it was given after that number was linked
/// (linking again, or another number, starts from nothing).
/// </summary>
public sealed class WhatsAppConsentService(KekeBeautyDbContext db, TimeProvider time)
{
    public static readonly Error NotLinked = new("consent.not_linked", "Liez d'abord votre numéro WhatsApp.");
    public static readonly Error StaleText = new("validation.failed", "Le texte d'accord a changé : rechargez la page puis recommencez.");
    public static readonly Error InvalidPurpose = new("validation.failed", "Type d'accord inconnu.");

    public async Task<WhatsAppConsentStatus> StatusAsync(Guid userId, CancellationToken ct)
    {
        var user = await LinkedAsync(userId, ct);
        return new WhatsAppConsentStatus(
            user is not null,
            user is null ? null : PhoneNumbers.Mask(user.Phone),
            user is null ? new ConsentState(false, null) : await StateAsync(user, ConsentPurpose.Service, ct),
            user is null ? new ConsentState(false, null) : await StateAsync(user, ConsentPurpose.Marketing, ct),
            WhatsAppConsentTexts.Version,
            WhatsAppConsentTexts.Service,
            WhatsAppConsentTexts.Marketing);
    }

    public async Task<Result<WhatsAppConsentStatus>> SetAsync(Guid userId, WhatsAppConsentInput input, string source, CancellationToken ct)
    {
        if (!Enum.TryParse<ConsentPurpose>(input.Purpose, ignoreCase: true, out var purpose) || !Enum.IsDefined(purpose))
        {
            return Result.Failure<WhatsAppConsentStatus>(InvalidPurpose);
        }

        var user = await LinkedAsync(userId, ct);
        if (user is null)
        {
            return Result.Failure<WhatsAppConsentStatus>(NotLinked);
        }

        // An agreement must be to the wording currently shown: the version the browser echoes proves which text the person saw.
        if (input.Granted && input.TextVersion != WhatsAppConsentTexts.Version)
        {
            return Result.Failure<WhatsAppConsentStatus>(StaleText);
        }

        var current = await StateAsync(user, purpose, ct);
        if (current.Granted != input.Granted)
        {
            await RecordAsync(userId, user.Phone, purpose, input.Granted, source, ct);
            await db.SaveChangesAsync(ct);
        }

        return Result.Success(await StatusAsync(userId, ct));
    }

    /// <summary>May this number receive messages of this kind? (used before every WhatsApp template send)</summary>
    public async Task<bool> IsGrantedAsync(Guid userId, string phone, ConsentPurpose purpose, CancellationToken ct)
    {
        var user = await LinkedAsync(userId, ct);
        return user is not null && user.Phone == phone && (await StateAsync(user, purpose, ct)).Granted;
    }

    /// <summary>A "STOP" reply: every agreement the number has is withdrawn (one history row per kind that was still granted).</summary>
    public async Task<int> WithdrawAllAsync(Guid userId, string phone, string source, CancellationToken ct)
    {
        var user = await LinkedAsync(userId, ct);
        if (user is null || user.Phone != phone)
        {
            return 0;
        }

        var withdrawn = 0;
        foreach (var purpose in Enum.GetValues<ConsentPurpose>())
        {
            if ((await StateAsync(user, purpose, ct)).Granted)
            {
                await RecordAsync(userId, phone, purpose, false, source, ct);
                withdrawn++;
            }
        }

        if (withdrawn > 0)
        {
            await db.SaveChangesAsync(ct);
        }

        return withdrawn;
    }

    private sealed record LinkedUser(string Phone, DateTimeOffset LinkedAt, Guid Id);

    private async Task<LinkedUser?> LinkedAsync(Guid userId, CancellationToken ct)
    {
        var row = await db.Users.AsNoTracking().Where(u => u.Id == userId && u.PhoneNumber != null && u.PhoneVerifiedAt != null)
            .Select(u => new { u.PhoneNumber, u.PhoneVerifiedAt }).FirstOrDefaultAsync(ct);
        return row is null ? null : new LinkedUser(row.PhoneNumber!, row.PhoneVerifiedAt!.Value, userId);
    }

    private async Task<ConsentState> StateAsync(LinkedUser user, ConsentPurpose purpose, CancellationToken ct)
    {
        var latest = await db.WhatsAppConsents.AsNoTracking()
            .Where(c => c.UserId == user.Id && c.Phone == user.Phone && c.Purpose == purpose && c.At >= user.LinkedAt)
            .OrderByDescending(c => c.At).Select(c => new { c.Granted, c.At }).FirstOrDefaultAsync(ct);
        return latest is null ? new ConsentState(false, null) : new ConsentState(latest.Granted, latest.At);
    }

    /// <summary>
    /// Appends a row. Its time is strictly later than the last row of the same person, number and kind, so that "the latest row" is never
    /// ambiguous (two clicks in the same instant, or a clock that moved back).
    /// </summary>
    private async Task RecordAsync(Guid userId, string phone, ConsentPurpose purpose, bool granted, string source, CancellationToken ct)
    {
        var last = await db.WhatsAppConsents.AsNoTracking().Where(c => c.UserId == userId && c.Phone == phone && c.Purpose == purpose)
            .OrderByDescending(c => c.At).Select(c => (DateTimeOffset?)c.At).FirstOrDefaultAsync(ct);
        // PostgreSQL keeps microseconds: compare and step in whole microseconds (10 ticks), as they will be stored.
        var now = time.GetUtcNow();
        var micro = new DateTimeOffset(now.Ticks / 10 * 10, now.Offset);
        var at = last is { } previous && previous >= micro ? new DateTimeOffset((previous.Ticks / 10 + 1) * 10, previous.Offset) : micro;
        db.WhatsAppConsents.Add(new WhatsAppConsent
        {
            UserId = userId,
            Phone = phone,
            Purpose = purpose,
            Granted = granted,
            TextVersion = WhatsAppConsentTexts.Version,
            TextSnapshot = WhatsAppConsentTexts.For(purpose),
            Source = source,
            At = at,
        });
    }
}
