using System.Text.Json;
using KekeBeauty.Infrastructure.Persistence;
using KekeBeauty.Modules.Administration.Domain;
using KekeBeauty.Modules.Catalog.Domain;
using KekeBeauty.Modules.Identity.Application;
using KekeBeauty.Modules.Onboarding.Domain;
using KekeBeauty.SharedKernel;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace KekeBeauty.Infrastructure.Partner;

/// <summary>Admin side of the onboarding: review queue, identity viewer, decisions (PRD ADM-03, ONB-02/03).</summary>
public sealed class ReviewService(
    KekeBeautyDbContext db,
    IFileStorage storage,
    IEmailSender email,
    TimeProvider time,
    IConfiguration configuration,
    ILogger<ReviewService> logger)
{
    public const int MaxPageSize = 50;

    public static readonly Error ReviewNotFound = new("review.not_found", "Dossier introuvable.");
    public static readonly Error DocumentNotFound = new("document.not_found", "Pièce introuvable.");
    public static readonly Error NotPending = new("review.not_pending", "Ce dossier a déjà fait l'objet d'une décision.");
    public static readonly Error IdentityNotValid = new("review.identity_not_valid", "La pièce d'identité du gérant n'est pas en attente de validation : demandez des corrections.");

    private readonly string _mediaBaseUrl = MediaUrls.BaseUrl(configuration);

    public async Task<QueuePage> QueueAsync(KycStatus status, int page, int pageSize, CancellationToken ct)
    {
        page = Math.Max(1, page);
        pageSize = Math.Clamp(pageSize, 1, MaxPageSize);

        var query =
            from r in db.EstablishmentReviews.AsNoTracking()
            where r.Status == status
            join e in db.Establishments.AsNoTracking() on r.EstablishmentId equals e.Id
            join u in db.Users.AsNoTracking() on e.OwnerUserId equals (Guid?)u.Id
            join c in db.Communes.AsNoTracking() on e.CommuneId equals c.Id
            select new { r.Id, r.SubmittedAt, r.Status, EstablishmentId = e.Id, e.Name, u.FirstName, u.LastName, u.Email, CommuneName = c.Name };

        var total = await query.CountAsync(ct);
        var rows = await query.OrderBy(x => x.SubmittedAt).Skip((page - 1) * pageSize).Take(pageSize).ToListAsync(ct);
        var items = rows.Select(x => new QueueItem(
                x.Id, x.EstablishmentId, x.Name, $"{x.FirstName} {x.LastName}".Trim(), x.Email, x.CommuneName, x.Status.ToString(), x.SubmittedAt))
            .ToList();
        return new QueuePage(items, total, page, pageSize);
    }

    public async Task<ReviewDetail?> GetAsync(Guid reviewId, CancellationToken ct)
    {
        var review = await db.EstablishmentReviews.AsNoTracking().FirstOrDefaultAsync(r => r.Id == reviewId, ct);
        if (review is null)
        {
            return null;
        }

        var e = await db.Establishments.AsNoTracking().Include(x => x.Categories).FirstAsync(x => x.Id == review.EstablishmentId, ct);
        var owner = e.OwnerUserId is { } ownerId
            ? await db.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Id == ownerId, ct)
            : null;
        var place = await (
            from c in db.Communes
            where c.Id == e.CommuneId
            join city in db.Cities on EF.Property<Guid>(c, "CityId") equals city.Id
            select new { CommuneName = c.Name, CityName = city.Name })
            .FirstAsync(ct);
        var categoryIds = e.Categories.Select(c => c.CategoryId).ToList();
        var categories = await db.BeautyCategories.AsNoTracking().Where(b => categoryIds.Contains(b.Id)).OrderBy(b => b.SortOrder)
            .Select(b => b.Name).ToListAsync(ct);
        var photos = (await db.MediaItems.AsNoTracking().Where(m => m.EstablishmentId == e.Id && m.Kind == MediaKind.Photo)
                .OrderByDescending(m => m.IsCover).ThenBy(m => m.SortOrder).Select(m => m.StorageKey).ToListAsync(ct))
            .Select(key => MediaUrls.Build(_mediaBaseUrl, key))
            .ToList();

        var identity = owner is null ? null : await db.KycIdentities.AsNoTracking().FirstOrDefaultAsync(k => k.UserId == owner.Id, ct);
        var checks = identity is null || owner is null
            ? []
            : PartnerText.EvaluateKyc(
                identity,
                $"{owner.FirstName} {owner.LastName}",
                await db.KycIdentities.AnyAsync(k => k.UserId != identity.UserId && k.DocumentType == identity.DocumentType && k.DocumentNumber == identity.DocumentNumber, ct),
                DateOnly.FromDateTime(time.GetUtcNow().UtcDateTime));

        return new ReviewDetail(
            review.Id, review.Status.ToString(), review.Comment, review.SubmittedAt,
            e.Id, e.Name, e.Description, e.CustomerServicePhone, e.Email, e.AddressLine, place.CommuneName, place.CityName,
            e.Latitude, e.Longitude, categories, photos,
            owner is null ? string.Empty : $"{owner.FirstName} {owner.LastName}".Trim(),
            owner?.Email ?? string.Empty,
            identity is null
                ? null
                : new ReviewIdentity(identity.Status.ToString(), identity.HolderName, identity.DocumentType.ToString(), identity.DocumentNumber,
                    identity.DocumentExpiry, identity.FrontKey is not null, identity.BackKey is not null, identity.SelfieKey is not null),
            checks);
    }

    /// <summary>Opens one identity document for an admin. Every access is recorded in the audit log (PRD ONB-03).</summary>
    public async Task<Result<StoredFile>> OpenDocumentAsync(Guid adminId, Guid reviewId, DocumentSide side, string? ip, CancellationToken ct)
    {
        var key = await (
            from r in db.EstablishmentReviews.AsNoTracking()
            where r.Id == reviewId
            join e in db.Establishments.AsNoTracking() on r.EstablishmentId equals e.Id
            join k in db.KycIdentities.AsNoTracking() on e.OwnerUserId equals (Guid?)k.UserId
            select side == DocumentSide.Front ? k.FrontKey : side == DocumentSide.Back ? k.BackKey : k.SelfieKey)
            .FirstOrDefaultAsync(ct);
        if (key is null)
        {
            return Result.Failure<StoredFile>(DocumentNotFound);
        }

        var file = await storage.OpenReadAsync(StorageBuckets.PrivateKyc, key, ct);
        if (file is null)
        {
            return Result.Failure<StoredFile>(DocumentNotFound);
        }

        db.AuditLogs.Add(new AuditLog
        {
            ActorUserId = adminId,
            Action = "kyc.document.view",
            EntityType = "EstablishmentReview",
            EntityId = reviewId,
            Data = JsonSerializer.Serialize(new { side = side.ToString() }),
            Ip = ip,
            At = time.GetUtcNow(),
        });
        await db.SaveChangesAsync(ct);
        return Result.Success(file);
    }

    public async Task<Result<ReviewInfo>> DecideAsync(Guid adminId, Guid reviewId, DecisionInput input, string? ip, CancellationToken ct)
    {
        var decision = input.Decision?.Trim().ToLowerInvariant();
        var comment = string.IsNullOrWhiteSpace(input.Comment) ? null : input.Comment.Trim();
        if (decision is not ("approve" or "reject" or "requestchanges"))
        {
            return Result.Failure<ReviewInfo>(PartnerErrors.Invalid("Décision inconnue (Approve, Reject ou RequestChanges)."));
        }

        if (decision != "approve" && comment is null)
        {
            return Result.Failure<ReviewInfo>(PartnerErrors.Invalid("Un motif ou un commentaire est obligatoire pour cette décision."));
        }

        if (comment is { Length: > 1000 })
        {
            return Result.Failure<ReviewInfo>(PartnerErrors.Invalid("Le commentaire est limité à 1000 caractères."));
        }

        var review = await db.EstablishmentReviews.FirstOrDefaultAsync(r => r.Id == reviewId, ct);
        if (review is null)
        {
            return Result.Failure<ReviewInfo>(ReviewNotFound);
        }

        if (review.Status != KycStatus.PendingReview)
        {
            return Result.Failure<ReviewInfo>(NotPending);
        }

        var establishment = await db.Establishments.FirstAsync(e => e.Id == review.EstablishmentId, ct);
        var ownerId = establishment.OwnerUserId;
        var identity = ownerId is null ? null : await db.KycIdentities.FirstOrDefaultAsync(k => k.UserId == ownerId, ct);
        var identityPending = identity?.Status == KycStatus.PendingReview;
        var now = time.GetUtcNow();

        switch (decision)
        {
            case "approve":
                if (identity is null || (!identityPending && identity.Status != KycStatus.Approved))
                {
                    return Result.Failure<ReviewInfo>(IdentityNotValid);
                }

                review.Status = KycStatus.Approved;
                establishment.Status = EstablishmentStatus.Active;
                Review(identity, KycStatus.Approved, null, identityPending);
                break;
            case "reject":
                review.Status = KycStatus.Rejected;
                establishment.Status = EstablishmentStatus.Rejected;
                Review(identity, KycStatus.Rejected, comment, identityPending);
                break;
            default:
                review.Status = KycStatus.ChangesRequested;
                establishment.Status = EstablishmentStatus.Draft;
                Review(identity, KycStatus.ChangesRequested, comment, identityPending);
                break;
        }

        review.Comment = comment;
        review.ReviewedBy = adminId;
        review.ReviewedAt = now;

        db.AuditLogs.Add(new AuditLog
        {
            ActorUserId = adminId,
            Action = "kyc.decision",
            EntityType = "EstablishmentReview",
            EntityId = reviewId,
            Data = JsonSerializer.Serialize(new { decision = review.Status.ToString(), hasComment = comment is not null }),
            Ip = ip,
            At = now,
        });
        await db.SaveChangesAsync(ct);

        if (review.Status == KycStatus.Approved)
        {
            // A published establishment can take bookings as soon as its owner enables them (PRD AGD-05).
            await Scheduling.ResourceService.EnsureDefaultAsync(db, establishment.Id, now, ct);
        }

        await NotifyOwnerAsync(ownerId, establishment.Name, review.Status, comment, ct);
        return Result.Success(new ReviewInfo(review.Status.ToString(), comment, review.SubmittedAt, now));

        void Review(KycIdentity? target, KycStatus status, string? note, bool applies)
        {
            if (target is null || !applies)
            {
                return;
            }

            target.Status = status;
            target.ReviewComment = note;
            target.ReviewedBy = adminId;
            target.ReviewedAt = now;
        }
    }

    private async Task NotifyOwnerAsync(Guid? ownerId, string establishmentName, KycStatus status, string? comment, CancellationToken ct)
    {
        if (ownerId is null)
        {
            return;
        }

        try
        {
            var owner = await db.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Id == ownerId, ct);
            if (owner is null)
            {
                return;
            }

            var (subject, body) = status switch
            {
                KycStatus.Approved => (
                    "Votre établissement est en ligne — Keke Beauty",
                    $"Bonne nouvelle : « {establishmentName} » a été validé et apparaît désormais dans l'annuaire Keke Beauty."),
                KycStatus.Rejected => (
                    "Votre dossier a été refusé — Keke Beauty",
                    $"Votre dossier pour « {establishmentName} » n'a pas pu être validé.\n\nMotif : {comment}"),
                _ => (
                    "Corrections demandées sur votre dossier — Keke Beauty",
                    $"Votre dossier pour « {establishmentName} » nécessite des corrections avant validation.\n\nCommentaire : {comment}\n\nConnectez-vous pour le modifier puis le soumettre à nouveau."),
            };
            await email.SendAsync(new EmailMessage(owner.Email, subject, body), ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // The decision is already saved; a notification failure must not undo or hide it.
            logger.LogWarning(ex, "Could not notify the owner about review decision {Status}", status);
        }
    }
}
