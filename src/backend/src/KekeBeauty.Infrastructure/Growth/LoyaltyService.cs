using KekeBeauty.Infrastructure.Partner;
using KekeBeauty.Infrastructure.Persistence;
using KekeBeauty.Infrastructure.Scheduling;
using KekeBeauty.Modules.Catalog.Domain;
using KekeBeauty.Modules.Scheduling.Domain;
using KekeBeauty.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace KekeBeauty.Infrastructure.Growth;

public sealed record LoyaltyProgramDto(bool IsActive, int PointsPer1000, int RewardThreshold, string RewardDescription, DateTimeOffset? ActiveSince);

public sealed record LoyaltyProgramInput(bool IsActive, int PointsPer1000, int RewardThreshold, string? RewardDescription);

/// <summary>What a visitor sees on the establishment page: how the card works.</summary>
public sealed record PublicLoyaltyDto(int PointsPer1000, int RewardThreshold, string RewardDescription);

public sealed record LoyaltyLineDto(string Kind, int Points, string? Note, DateTimeOffset At);

public sealed record LoyaltyCardDto(
    Guid EstablishmentId,
    string EstablishmentName,
    string EstablishmentSlug,
    int Points,
    int RewardThreshold,
    string RewardDescription,
    int RewardsAvailable,
    bool ProgramActive,
    IReadOnlyList<LoyaltyLineDto> History);

public sealed record LoyaltyMemberDto(Guid ClientUserId, string ClientName, int Points, int RewardsAvailable, DateTimeOffset LastActivityAt);

public sealed record LoyaltyRedeemInput(Guid ClientUserId);

/// <summary>
/// Loyalty cards with points: each finished visit earns points in proportion to its price, and a reward is granted once the
/// threshold is reached. Earnings are recorded idempotently from the finished visits (by the maintenance job right after a
/// visit, and again whenever a card is read), so a missed run never loses points.
/// </summary>
public sealed class LoyaltyService(KekeBeautyDbContext db, NotificationService notifications, TimeProvider time)
{
    public const int MinPointsPer1000 = 1;
    public const int MaxPointsPer1000 = 100;
    public const int MinThreshold = 10;
    public const int MaxThreshold = 100_000;
    private const int HistoryLength = 20;

    private static readonly Error NoProgram = new("loyalty.no_program", "Cet établissement n'a pas de carte de fidélité active.");
    private static readonly Error NotEnoughPoints = new("loyalty.not_enough_points", "Cette cliente n'a pas encore assez de points pour une récompense.");

    /// <summary>Points earned by a visit at the given price: every full 1 000 earns the rate.</summary>
    public static int PointsFor(decimal price, int pointsPer1000) => price <= 0 ? 0 : (int)Math.Floor(price / 1000m) * pointsPer1000;

    // ───────── Establishment ─────────

    public async Task<Result<LoyaltyProgramDto?>> GetProgramAsync(Guid ownerId, Guid establishmentId, CancellationToken ct)
    {
        if (!await PartnerSupport.OwnedBy(db, ownerId, establishmentId).AnyAsync(ct))
        {
            return Result.Failure<LoyaltyProgramDto?>(PartnerErrors.NotFound);
        }

        var program = await db.LoyaltyPrograms.AsNoTracking().FirstOrDefaultAsync(p => p.EstablishmentId == establishmentId, ct);
        return Result.Success(program is null ? null : ToDto(program));
    }

    public async Task<Result<LoyaltyProgramDto>> SaveProgramAsync(Guid ownerId, Guid establishmentId, LoyaltyProgramInput input, CancellationToken ct)
    {
        if (!await PartnerSupport.OwnedBy(db, ownerId, establishmentId).AnyAsync(ct))
        {
            return Result.Failure<LoyaltyProgramDto>(PartnerErrors.NotFound);
        }

        var description = input.RewardDescription?.Trim();
        if (input.PointsPer1000 is < MinPointsPer1000 or > MaxPointsPer1000)
        {
            return Result.Failure<LoyaltyProgramDto>(PartnerErrors.Invalid($"Les points gagnés par tranche de 1 000 F doivent être compris entre {MinPointsPer1000} et {MaxPointsPer1000}."));
        }

        if (input.RewardThreshold is < MinThreshold or > MaxThreshold)
        {
            return Result.Failure<LoyaltyProgramDto>(PartnerErrors.Invalid($"Le seuil de récompense doit être compris entre {MinThreshold} et {MaxThreshold} points."));
        }

        if (description is not { Length: >= 3 and <= 200 })
        {
            return Result.Failure<LoyaltyProgramDto>(PartnerErrors.Invalid("Décrivez la récompense en 3 à 200 caractères."));
        }

        var now = time.GetUtcNow();
        var program = await db.LoyaltyPrograms.FirstOrDefaultAsync(p => p.EstablishmentId == establishmentId, ct);
        if (program is null)
        {
            program = new LoyaltyProgram { EstablishmentId = establishmentId, RewardDescription = description };
            db.LoyaltyPrograms.Add(program);
        }

        // Visits made while the card was switched off never earn points afterwards: the card (re)starts now.
        if (input.IsActive && !program.IsActive)
        {
            program.ActiveSince = now;
        }

        program.IsActive = input.IsActive;
        program.PointsPer1000 = input.PointsPer1000;
        program.RewardThreshold = input.RewardThreshold;
        program.RewardDescription = description;
        program.UpdatedAt = now;
        await db.SaveChangesAsync(ct);
        return Result.Success(ToDto(program));
    }

    public async Task<Result<IReadOnlyList<LoyaltyMemberDto>>> MembersAsync(Guid ownerId, Guid establishmentId, CancellationToken ct)
    {
        if (!await PartnerSupport.OwnedBy(db, ownerId, establishmentId).AnyAsync(ct))
        {
            return Result.Failure<IReadOnlyList<LoyaltyMemberDto>>(PartnerErrors.NotFound);
        }

        await SyncAsync(establishmentId, null, ct);
        var threshold = await db.LoyaltyPrograms.AsNoTracking().Where(p => p.EstablishmentId == establishmentId).Select(p => (int?)p.RewardThreshold).FirstOrDefaultAsync(ct);
        var balances = await db.LoyaltyTransactions.AsNoTracking()
            .Where(t => t.EstablishmentId == establishmentId)
            .GroupBy(t => t.ClientUserId)
            .Select(g => new { ClientUserId = g.Key, Points = g.Sum(t => t.Points), Last = g.Max(t => t.CreatedAt) })
            .OrderByDescending(b => b.Points).ThenByDescending(b => b.Last)
            .Take(500)
            .ToListAsync(ct);
        var ids = balances.Select(b => b.ClientUserId).ToList();
        var names = await db.Users.AsNoTracking().Where(u => ids.Contains(u.Id)).ToDictionaryAsync(u => u.Id, u => $"{u.FirstName} {u.LastName}".Trim(), ct);
        return Result.Success<IReadOnlyList<LoyaltyMemberDto>>(balances
            .Select(b => new LoyaltyMemberDto(b.ClientUserId, names.GetValueOrDefault(b.ClientUserId) ?? "Cliente", b.Points, Rewards(b.Points, threshold), b.Last))
            .ToList());
    }

    /// <summary>The establishment granted the reward at the counter: the threshold is taken off the client's card.</summary>
    public async Task<Result<LoyaltyMemberDto>> RedeemAsync(Guid ownerId, Guid establishmentId, LoyaltyRedeemInput input, CancellationToken ct)
    {
        var establishment = await PartnerSupport.OwnedBy(db, ownerId, establishmentId).Select(e => new { e.Name }).FirstOrDefaultAsync(ct);
        if (establishment is null)
        {
            return Result.Failure<LoyaltyMemberDto>(PartnerErrors.NotFound);
        }

        var program = await db.LoyaltyPrograms.AsNoTracking().FirstOrDefaultAsync(p => p.EstablishmentId == establishmentId && p.IsActive, ct);
        if (program is null)
        {
            return Result.Failure<LoyaltyMemberDto>(NoProgram);
        }

        await SyncAsync(establishmentId, input.ClientUserId, ct);

        // Two counters granting the same reward at once must not overdraw the card: one at a time per client and establishment.
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        await db.Database.ExecuteSqlInterpolatedAsync(
            $"SELECT pg_advisory_xact_lock(hashtextextended({"loyalty:" + establishmentId + ":" + input.ClientUserId}, 0))", ct);
        var points = await Balance(establishmentId, input.ClientUserId).SumAsync(t => t.Points, ct);
        if (points < program.RewardThreshold)
        {
            return Result.Failure<LoyaltyMemberDto>(NotEnoughPoints);
        }

        var now = time.GetUtcNow();
        db.LoyaltyTransactions.Add(new LoyaltyTransaction
        {
            EstablishmentId = establishmentId,
            ClientUserId = input.ClientUserId,
            Kind = LoyaltyTransactionKind.Redeem,
            Points = -program.RewardThreshold,
            Note = program.RewardDescription,
            CreatedByUserId = ownerId,
            CreatedAt = now,
        });
        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);

        var left = points - program.RewardThreshold;
        await notifications.NotifyAsync(input.ClientUserId, "loyalty.redeemed", "Récompense fidélité utilisée",
            $"{establishment.Name} vous a accordé votre récompense : {program.RewardDescription}. Il vous reste {left} points.", null, ct);
        var name = await db.Users.AsNoTracking().Where(u => u.Id == input.ClientUserId).Select(u => $"{u.FirstName} {u.LastName}").FirstOrDefaultAsync(ct);
        return Result.Success(new LoyaltyMemberDto(input.ClientUserId, name?.Trim() ?? "Cliente", left, Rewards(left, program.RewardThreshold), now));
    }

    // ───────── Client and visitors ─────────

    public async Task<PublicLoyaltyDto?> PublicAsync(string slug, CancellationToken ct) =>
        await (from p in db.LoyaltyPrograms.AsNoTracking()
               join e in db.Establishments on p.EstablishmentId equals e.Id
               where e.Slug == slug && e.Status == EstablishmentStatus.Active && p.IsActive
               select new PublicLoyaltyDto(p.PointsPer1000, p.RewardThreshold, p.RewardDescription)).FirstOrDefaultAsync(ct);

    /// <summary>Every card of the client: one per establishment where she earned or spent points.</summary>
    public async Task<IReadOnlyList<LoyaltyCardDto>> MineAsync(Guid clientId, CancellationToken ct)
    {
        await SyncAsync(null, clientId, ct);
        var lines = await db.LoyaltyTransactions.AsNoTracking().Where(t => t.ClientUserId == clientId).OrderByDescending(t => t.CreatedAt).ToListAsync(ct);
        if (lines.Count == 0)
        {
            return [];
        }

        var establishmentIds = lines.Select(t => t.EstablishmentId).Distinct().ToList();
        var establishments = await db.Establishments.AsNoTracking().Where(e => establishmentIds.Contains(e.Id))
            .ToDictionaryAsync(e => e.Id, e => new { e.Name, e.Slug }, ct);
        var programs = await db.LoyaltyPrograms.AsNoTracking().Where(p => establishmentIds.Contains(p.EstablishmentId)).ToDictionaryAsync(p => p.EstablishmentId, ct);
        return lines.GroupBy(t => t.EstablishmentId)
            .Where(g => establishments.ContainsKey(g.Key))
            .Select(g =>
            {
                var program = programs.GetValueOrDefault(g.Key);
                var points = g.Sum(t => t.Points);
                var e = establishments[g.Key];
                return new LoyaltyCardDto(
                    g.Key, e.Name, e.Slug, points,
                    program?.RewardThreshold ?? 0,
                    program?.RewardDescription ?? string.Empty,
                    program?.IsActive == true ? Rewards(points, program.RewardThreshold) : 0,
                    program?.IsActive == true,
                    g.Where(t => t.Points != 0).Take(HistoryLength).Select(t => new LoyaltyLineDto(t.Kind.ToString(), t.Points, t.Note, t.CreatedAt)).ToList());
            })
            .OrderByDescending(c => c.RewardsAvailable).ThenByDescending(c => c.Points)
            .ToList();
    }

    // ───────── Earning ─────────

    /// <summary>
    /// Records the points of every finished visit that has not earned yet, for one establishment and/or one client (or all).
    /// Safe to run concurrently: the unique index on the visit keeps a single earning. Returns how many visits earned.
    /// </summary>
    public async Task<int> SyncAsync(Guid? establishmentId, Guid? clientId, CancellationToken ct)
    {
        var query =
            from a in db.Appointments.AsNoTracking()
            join p in db.LoyaltyPrograms.AsNoTracking() on a.EstablishmentId equals p.EstablishmentId
            where p.IsActive && p.ActiveSince != null && a.StartsAt >= p.ActiveSince
                && a.Status == AppointmentStatus.Completed && a.ClientUserId != null && a.WalkInName == null
                && !db.LoyaltyTransactions.Any(t => t.AppointmentId == a.Id && t.Kind == LoyaltyTransactionKind.Earn)
            select new { a.Id, a.EstablishmentId, ClientUserId = a.ClientUserId!.Value, a.TotalPrice, a.EndsAt, p.PointsPer1000 };
        if (establishmentId is { } onlyEstablishment)
        {
            query = query.Where(x => x.EstablishmentId == onlyEstablishment);
        }

        if (clientId is { } onlyClient)
        {
            query = query.Where(x => x.ClientUserId == onlyClient);
        }

        var due = await query.Take(500).ToListAsync(ct);
        if (due.Count == 0)
        {
            return 0;
        }

        var names = await db.Establishments.AsNoTracking().Where(e => due.Select(d => d.EstablishmentId).Contains(e.Id)).ToDictionaryAsync(e => e.Id, e => e.Name, ct);
        var earned = 0;
        foreach (var visit in due)
        {
            db.LoyaltyTransactions.Add(new LoyaltyTransaction
            {
                EstablishmentId = visit.EstablishmentId,
                ClientUserId = visit.ClientUserId,
                Kind = LoyaltyTransactionKind.Earn,
                Points = PointsFor(visit.TotalPrice, visit.PointsPer1000),
                AppointmentId = visit.Id,
                Note = $"Visite chez {names.GetValueOrDefault(visit.EstablishmentId)}",
                CreatedAt = visit.EndsAt,
            });
            try
            {
                await db.SaveChangesAsync(ct);
                earned++;
            }
            catch (DbUpdateException)
            {
                // Recorded meanwhile by another request.
                db.ChangeTracker.Clear();
            }
        }

        return earned;
    }

    private IQueryable<LoyaltyTransaction> Balance(Guid establishmentId, Guid clientId) =>
        db.LoyaltyTransactions.Where(t => t.EstablishmentId == establishmentId && t.ClientUserId == clientId);

    private static int Rewards(int points, int? threshold) => threshold is > 0 && points > 0 ? points / threshold.Value : 0;

    private static LoyaltyProgramDto ToDto(LoyaltyProgram p) => new(p.IsActive, p.PointsPer1000, p.RewardThreshold, p.RewardDescription, p.ActiveSince);
}
