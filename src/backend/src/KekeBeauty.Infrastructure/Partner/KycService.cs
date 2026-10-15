using KekeBeauty.Infrastructure.Persistence;
using KekeBeauty.Modules.Onboarding.Domain;
using KekeBeauty.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace KekeBeauty.Infrastructure.Partner;

/// <summary>The partner's own identity file (PRD ONB-01 step 5, ONB-03). Documents go to the private bucket only.</summary>
public sealed class KycService(KekeBeautyDbContext db, IFileStorage storage, TimeProvider time)
{
    private static readonly Error Locked = new("kyc.locked", "Votre pièce d'identité est en cours d'examen ou déjà approuvée : elle ne peut plus être modifiée.");

    public async Task<KycView?> GetAsync(Guid userId, CancellationToken ct)
    {
        var identity = await db.KycIdentities.AsNoTracking().FirstOrDefaultAsync(k => k.UserId == userId, ct);
        return identity is null ? null : ToView(identity);
    }

    public async Task<Result<KycView>> UpsertAsync(Guid userId, KycInput input, CancellationToken ct)
    {
        var holder = input.HolderName?.Trim() ?? string.Empty;
        if (holder.Length is < 3 or > 150)
        {
            return Result.Failure<KycView>(PartnerErrors.Invalid("Le nom figurant sur la pièce doit contenir entre 3 et 150 caractères."));
        }

        if (!Enum.TryParse<IdentityDocumentType>(input.DocumentType, ignoreCase: true, out var type) || !Enum.IsDefined(type))
        {
            return Result.Failure<KycView>(PartnerErrors.Invalid("Type de pièce inconnu (NationalId, Passport, DrivingLicense, ConsularCard)."));
        }

        var number = PartnerText.NormalizeDocumentNumber(input.DocumentNumber ?? string.Empty);
        if (number.Length is < 4 or > 30)
        {
            return Result.Failure<KycView>(PartnerErrors.Invalid("Le numéro de la pièce doit contenir entre 4 et 30 caractères."));
        }

        var today = DateOnly.FromDateTime(time.GetUtcNow().UtcDateTime);
        if (input.DocumentExpiry is { } expiry && (expiry.Year < 1950 || expiry > today.AddYears(25)))
        {
            return Result.Failure<KycView>(PartnerErrors.Invalid("Date d'expiration invalide."));
        }

        var identity = await db.KycIdentities.FirstOrDefaultAsync(k => k.UserId == userId, ct);
        if (identity is null)
        {
            identity = new KycIdentity { UserId = userId, CreatedAt = time.GetUtcNow() };
            db.KycIdentities.Add(identity);
        }
        else if (!identity.IsEditable)
        {
            return Result.Failure<KycView>(Locked);
        }

        identity.HolderName = holder;
        identity.DocumentType = type;
        identity.DocumentNumber = number;
        identity.DocumentExpiry = input.DocumentExpiry;
        identity.Status = KycStatus.Draft;
        await db.SaveChangesAsync(ct);
        return Result.Success(ToView(identity));
    }

    public async Task<Result<KycView>> UploadDocumentAsync(Guid userId, DocumentSide side, Stream content, CancellationToken ct)
    {
        var identity = await db.KycIdentities.FirstOrDefaultAsync(k => k.UserId == userId, ct);
        if (identity is null)
        {
            return Result.Failure<KycView>(PartnerErrors.Invalid("Renseignez d'abord le type et le numéro de la pièce."));
        }

        if (!identity.IsEditable)
        {
            return Result.Failure<KycView>(Locked);
        }

        var image = await ImageProcessor.ProcessAsync(content, ImageProcessor.DocumentWidth, ct);
        if (image.IsFailure)
        {
            return Result.Failure<KycView>(image.Error);
        }

        var key = $"kyc/{userId}/{side.ToString().ToLowerInvariant()}-{Guid.CreateVersion7()}.webp";
        using (var stream = new MemoryStream(image.Value.Bytes))
        {
            await storage.PutAsync(StorageBuckets.PrivateKyc, key, stream, ImageProcessor.WebpContentType, ct);
        }

        var previous = identity.KeyOf(side);
        switch (side)
        {
            case DocumentSide.Front:
                identity.FrontKey = key;
                break;
            case DocumentSide.Back:
                identity.BackKey = key;
                break;
            default:
                identity.SelfieKey = key;
                break;
        }

        identity.Status = KycStatus.Draft;
        await db.SaveChangesAsync(ct);

        if (previous is not null)
        {
            try
            {
                await storage.DeleteAsync(StorageBuckets.PrivateKyc, previous, ct);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                // The replaced file becomes unreachable (no database reference); a storage hiccup must not fail the upload.
            }
        }

        return Result.Success(ToView(identity));
    }

    private static KycView ToView(KycIdentity k) => new(
        k.Status.ToString(),
        k.IsEditable,
        k.HolderName,
        k.DocumentType.ToString(),
        k.DocumentNumber,
        k.DocumentExpiry,
        k.FrontKey is not null,
        k.BackKey is not null,
        k.SelfieKey is not null,
        k.ReviewComment);
}
