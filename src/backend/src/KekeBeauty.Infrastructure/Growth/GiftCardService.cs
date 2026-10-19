using System.Security.Cryptography;
using KekeBeauty.Infrastructure.Partner;
using KekeBeauty.Infrastructure.Persistence;
using KekeBeauty.Infrastructure.Scheduling;
using KekeBeauty.Modules.Scheduling.Domain;
using KekeBeauty.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace KekeBeauty.Infrastructure.Growth;

public sealed record GiftCardDto(
    Guid Id,
    Guid EstablishmentId,
    string EstablishmentName,
    string EstablishmentSlug,
    string Code,
    decimal InitialAmount,
    decimal Balance,
    string CurrencyCode,
    string RecipientName,
    string? BuyerName,
    string? Message,
    string Status,
    DateTimeOffset ExpiresAt,
    DateTimeOffset CreatedAt,
    bool HasHolder,
    IReadOnlyList<GiftCardRedemptionDto> Redemptions);

public sealed record GiftCardRedemptionDto(decimal Amount, DateTimeOffset At);

public sealed record CreateGiftCardInput(decimal Amount, string? RecipientName, string? BuyerName, string? Message, int ValidityMonths = 12);

public sealed record RedeemGiftCardInput(decimal Amount, Guid? AppointmentId);

public sealed record ClaimGiftCardInput(string? Code);

/// <summary>
/// Gift cards sold by an establishment. The establishment collects the money itself (cash or mobile money, like any of its
/// services); the platform issues an unguessable code and keeps the balance as the card is spent over one or more visits.
/// </summary>
public sealed class GiftCardService(KekeBeautyDbContext db, NotificationService notifications, TimeProvider time)
{
    public const decimal MinAmount = 1_000;
    public const decimal MaxAmount = 1_000_000;
    public const int MaxValidityMonths = 24;

    // No 0/O, 1/I/L: the code is read aloud and typed at a counter.
    private const string Alphabet = "23456789ABCDEFGHJKMNPQRSTUVWXYZ";

    private static readonly Error NotFound = new("gift_card.not_found", "Carte cadeau introuvable.");
    private static readonly Error NotUsable = new("gift_card.not_usable", "Cette carte cadeau est annulée ou expirée.");
    private static readonly Error InsufficientBalance = new("gift_card.insufficient_balance", "Le solde de la carte cadeau est insuffisant pour ce montant.");
    private static readonly Error AlreadyClaimed = new("gift_card.already_claimed", "Cette carte cadeau est déjà rattachée à un autre compte.");

    /// <summary>A new code such as <c>KB-7HQ4-M2XP</c>: 8 random characters out of 31, about 40 bits.</summary>
    public static string NewCode()
    {
        Span<char> chars = stackalloc char[8];
        for (var i = 0; i < chars.Length; i++)
        {
            chars[i] = Alphabet[RandomNumberGenerator.GetInt32(Alphabet.Length)];
        }

        return $"KB-{new string(chars[..4])}-{new string(chars[4..])}";
    }

    /// <summary>Accepts what people type: lower case, spaces, missing dashes.</summary>
    public static string? NormalizeCode(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return null;
        }

        var compact = new string(raw.Where(char.IsLetterOrDigit).Select(char.ToUpperInvariant).ToArray());
        if (compact.StartsWith("KB", StringComparison.Ordinal))
        {
            compact = compact[2..];
        }

        return compact.Length == 8 && compact.All(c => Alphabet.Contains(c)) ? $"KB-{compact[..4]}-{compact[4..]}" : null;
    }

    // ───────── Establishment ─────────

    public async Task<Result<GiftCardDto>> CreateAsync(Guid ownerId, Guid establishmentId, CreateGiftCardInput input, CancellationToken ct)
    {
        if (!await PartnerSupport.OwnedBy(db, ownerId, establishmentId).AnyAsync(ct))
        {
            return Result.Failure<GiftCardDto>(PartnerErrors.NotFound);
        }

        var recipient = input.RecipientName?.Trim();
        var buyer = string.IsNullOrWhiteSpace(input.BuyerName) ? null : input.BuyerName.Trim();
        var message = string.IsNullOrWhiteSpace(input.Message) ? null : input.Message.Trim();
        if (input.Amount is < MinAmount or > MaxAmount || decimal.Round(input.Amount, 0) != input.Amount)
        {
            return Result.Failure<GiftCardDto>(PartnerErrors.Invalid($"Le montant doit être un nombre entier de francs entre {MinAmount:0} et {MaxAmount:0}."));
        }

        if (recipient is not { Length: >= 2 and <= 80 } || buyer is { Length: > 80 } || message is { Length: > 300 })
        {
            return Result.Failure<GiftCardDto>(PartnerErrors.Invalid("Indiquez le nom de la personne qui reçoit la carte (2 à 80 caractères) ; le message est limité à 300 caractères."));
        }

        if (input.ValidityMonths is < 1 or > MaxValidityMonths)
        {
            return Result.Failure<GiftCardDto>(PartnerErrors.Invalid($"La validité doit être comprise entre 1 et {MaxValidityMonths} mois."));
        }

        var now = time.GetUtcNow();
        for (var attempt = 0; ; attempt++)
        {
            var card = new GiftCard
            {
                EstablishmentId = establishmentId,
                Code = NewCode(),
                InitialAmount = input.Amount,
                Balance = input.Amount,
                RecipientName = recipient,
                BuyerName = buyer,
                Message = message,
                ExpiresAt = now.AddMonths(input.ValidityMonths),
                CreatedByUserId = ownerId,
                CreatedAt = now,
            };
            db.GiftCards.Add(card);
            try
            {
                await db.SaveChangesAsync(ct);
                return Result.Success((await ProjectAsync([card], ct))[0]);
            }
            catch (DbUpdateException) when (attempt < 3)
            {
                // A code collision (one in a trillion): draw another.
                db.ChangeTracker.Clear();
            }
        }
    }

    public async Task<Result<IReadOnlyList<GiftCardDto>>> ListAsync(Guid ownerId, Guid establishmentId, CancellationToken ct)
    {
        if (!await PartnerSupport.OwnedBy(db, ownerId, establishmentId).AnyAsync(ct))
        {
            return Result.Failure<IReadOnlyList<GiftCardDto>>(PartnerErrors.NotFound);
        }

        var cards = await db.GiftCards.AsNoTracking().Where(g => g.EstablishmentId == establishmentId).OrderByDescending(g => g.CreatedAt).Take(300).ToListAsync(ct);
        return Result.Success(await ProjectAsync(cards, ct));
    }

    /// <summary>The counter types the code a client shows: only cards of this establishment are found.</summary>
    public async Task<Result<GiftCardDto>> LookupAsync(Guid ownerId, Guid establishmentId, string? code, CancellationToken ct)
    {
        if (!await PartnerSupport.OwnedBy(db, ownerId, establishmentId).AnyAsync(ct))
        {
            return Result.Failure<GiftCardDto>(PartnerErrors.NotFound);
        }

        var normalized = NormalizeCode(code);
        var card = normalized is null ? null
            : await db.GiftCards.AsNoTracking().FirstOrDefaultAsync(g => g.Code == normalized && g.EstablishmentId == establishmentId, ct);
        return card is null ? Result.Failure<GiftCardDto>(NotFound) : Result.Success((await ProjectAsync([card], ct))[0]);
    }

    public async Task<Result<GiftCardDto>> RedeemAsync(Guid ownerId, Guid cardId, RedeemGiftCardInput input, CancellationToken ct)
    {
        var card = await OwnedCard(ownerId, cardId).AsNoTracking().FirstOrDefaultAsync(ct);
        if (card is null)
        {
            return Result.Failure<GiftCardDto>(NotFound);
        }

        var now = time.GetUtcNow();
        if (card.Status != GiftCardStatus.Active || card.ExpiresAt <= now)
        {
            return Result.Failure<GiftCardDto>(NotUsable);
        }

        if (input.Amount <= 0 || decimal.Round(input.Amount, 0) != input.Amount)
        {
            return Result.Failure<GiftCardDto>(PartnerErrors.Invalid("Le montant à débiter doit être un nombre entier de francs, supérieur à 0."));
        }

        if (input.AppointmentId is { } appointmentId
            && !await db.Appointments.AnyAsync(a => a.Id == appointmentId && a.EstablishmentId == card.EstablishmentId, ct))
        {
            return Result.Failure<GiftCardDto>(SchedulingErrors.AppointmentNotFound);
        }

        // Spend atomically: two counters debiting the same card at once can never take more than the balance.
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        var debited = await db.GiftCards
            .Where(g => g.Id == card.Id && g.Status == GiftCardStatus.Active && g.Balance >= input.Amount)
            .ExecuteUpdateAsync(set => set.SetProperty(g => g.Balance, g => g.Balance - input.Amount), ct);
        if (debited == 0)
        {
            return Result.Failure<GiftCardDto>(InsufficientBalance);
        }

        db.GiftCardRedemptions.Add(new GiftCardRedemption
        {
            GiftCardId = card.Id,
            Amount = input.Amount,
            AppointmentId = input.AppointmentId,
            CreatedByUserId = ownerId,
            CreatedAt = now,
        });
        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);

        var updated = await db.GiftCards.AsNoTracking().FirstAsync(g => g.Id == card.Id, ct);
        if (updated.HolderUserId is { } holder)
        {
            var name = await db.Establishments.AsNoTracking().Where(e => e.Id == updated.EstablishmentId).Select(e => e.Name).FirstAsync(ct);
            await notifications.NotifyAsync(holder, "gift_card.redeemed", "Carte cadeau utilisée",
                $"{input.Amount:0} F ont été débités de votre carte cadeau {updated.Code} chez {name}. Solde restant : {updated.Balance:0} F.", null, ct);
        }

        return Result.Success((await ProjectAsync([updated], ct))[0]);
    }

    public async Task<Result<GiftCardDto>> CancelAsync(Guid ownerId, Guid cardId, CancellationToken ct)
    {
        var card = await OwnedCard(ownerId, cardId).FirstOrDefaultAsync(ct);
        if (card is null)
        {
            return Result.Failure<GiftCardDto>(NotFound);
        }

        if (card.Status == GiftCardStatus.Active)
        {
            card.Status = GiftCardStatus.Cancelled;
            await db.SaveChangesAsync(ct);
        }

        return Result.Success((await ProjectAsync([card], ct))[0]);
    }

    // ───────── Client ─────────

    /// <summary>The client adds a card she received to her account, to follow its balance.</summary>
    public async Task<Result<GiftCardDto>> ClaimAsync(Guid clientId, ClaimGiftCardInput input, CancellationToken ct)
    {
        var code = NormalizeCode(input.Code);
        var card = code is null ? null : await db.GiftCards.FirstOrDefaultAsync(g => g.Code == code, ct);
        if (card is null)
        {
            return Result.Failure<GiftCardDto>(NotFound);
        }

        if (card.HolderUserId is { } holder && holder != clientId)
        {
            return Result.Failure<GiftCardDto>(AlreadyClaimed);
        }

        card.HolderUserId = clientId;
        await db.SaveChangesAsync(ct);
        return Result.Success((await ProjectAsync([card], ct))[0]);
    }

    public async Task<IReadOnlyList<GiftCardDto>> MineAsync(Guid clientId, CancellationToken ct) =>
        await ProjectAsync(await db.GiftCards.AsNoTracking().Where(g => g.HolderUserId == clientId).OrderByDescending(g => g.CreatedAt).Take(50).ToListAsync(ct), ct);

    // ───────── Projection ─────────

    private IQueryable<GiftCard> OwnedCard(Guid ownerId, Guid cardId) =>
        from g in db.GiftCards
        join e in db.Establishments on g.EstablishmentId equals e.Id
        where g.Id == cardId && e.OwnerUserId == ownerId
        select g;

    private async Task<IReadOnlyList<GiftCardDto>> ProjectAsync(IReadOnlyList<GiftCard> cards, CancellationToken ct)
    {
        if (cards.Count == 0)
        {
            return [];
        }

        var ids = cards.Select(g => g.Id).ToList();
        var establishmentIds = cards.Select(g => g.EstablishmentId).Distinct().ToList();
        var establishments = await db.Establishments.AsNoTracking().Where(e => establishmentIds.Contains(e.Id)).ToDictionaryAsync(e => e.Id, e => new { e.Name, e.Slug }, ct);
        var redemptions = (await db.GiftCardRedemptions.AsNoTracking().Where(r => ids.Contains(r.GiftCardId)).OrderByDescending(r => r.CreatedAt).ToListAsync(ct))
            .GroupBy(r => r.GiftCardId)
            .ToDictionary(g => g.Key, g => (IReadOnlyList<GiftCardRedemptionDto>)g.Select(r => new GiftCardRedemptionDto(r.Amount, r.CreatedAt)).ToList());
        var now = time.GetUtcNow();
        return cards.Select(g =>
        {
            var e = establishments[g.EstablishmentId];
            var status = g.Status == GiftCardStatus.Cancelled ? "Cancelled"
                : g.ExpiresAt <= now ? "Expired"
                : g.Balance == 0 ? "Spent"
                : "Active";
            return new GiftCardDto(
                g.Id, g.EstablishmentId, e.Name, e.Slug, g.Code, g.InitialAmount, g.Balance, g.CurrencyCode,
                g.RecipientName, g.BuyerName, g.Message, status, g.ExpiresAt, g.CreatedAt, g.HolderUserId != null,
                redemptions.GetValueOrDefault(g.Id) ?? []);
        }).ToList();
    }
}
