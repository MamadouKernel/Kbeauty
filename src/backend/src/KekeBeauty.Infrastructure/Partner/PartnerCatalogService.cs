using KekeBeauty.Infrastructure.Persistence;
using KekeBeauty.Modules.Catalog.Domain;
using KekeBeauty.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace KekeBeauty.Infrastructure.Partner;

/// <summary>Services and service groups of an establishment (PRD SVC-01/02). Deletion of a service is logical.</summary>
public sealed class PartnerCatalogService(
    KekeBeautyDbContext db,
    TimeProvider time,
    KekeBeauty.Infrastructure.Billing.EntitlementService? entitlements = null)
{
    public const int MaxServices = 100;
    public const int MaxGroups = 30;
    private const decimal MaxPrice = 10_000_000m;

    private static readonly Error ServiceNotFound = new("service.not_found", "Prestation introuvable.");
    private static readonly Error GroupNotFound = new("group.not_found", "Rubrique introuvable.");

    // ───────── Groups ─────────

    public async Task<Result<Guid>> AddGroupAsync(Guid userId, Guid establishmentId, GroupInput input, CancellationToken ct)
    {
        var gate = await EditableAsync(userId, establishmentId, ct);
        if (gate is not null)
        {
            return Result.Failure<Guid>(gate);
        }

        var name = input.Name?.Trim() ?? string.Empty;
        if (name.Length is < 2 or > 120)
        {
            return Result.Failure<Guid>(PartnerErrors.Invalid("Le nom de la rubrique doit contenir entre 2 et 120 caractères."));
        }

        var count = await db.ServiceGroups.CountAsync(g => g.EstablishmentId == establishmentId, ct);
        if (count >= MaxGroups)
        {
            return Result.Failure<Guid>(PartnerErrors.Invalid($"Au plus {MaxGroups} rubriques."));
        }

        var group = new ServiceGroup { EstablishmentId = establishmentId, Name = name, SortOrder = count };
        db.ServiceGroups.Add(group);
        await db.SaveChangesAsync(ct);
        return Result.Success(group.Id);
    }

    public async Task<Result> RenameGroupAsync(Guid userId, Guid establishmentId, Guid groupId, GroupInput input, CancellationToken ct)
    {
        var gate = await EditableAsync(userId, establishmentId, ct);
        if (gate is not null)
        {
            return Result.Failure(gate);
        }

        var name = input.Name?.Trim() ?? string.Empty;
        if (name.Length is < 2 or > 120)
        {
            return Result.Failure(PartnerErrors.Invalid("Le nom de la rubrique doit contenir entre 2 et 120 caractères."));
        }

        var group = await db.ServiceGroups.FirstOrDefaultAsync(g => g.Id == groupId && g.EstablishmentId == establishmentId, ct);
        if (group is null)
        {
            return Result.Failure(GroupNotFound);
        }

        group.Name = name;
        await db.SaveChangesAsync(ct);
        return Result.Success();
    }

    public async Task<Result> DeleteGroupAsync(Guid userId, Guid establishmentId, Guid groupId, CancellationToken ct)
    {
        var gate = await EditableAsync(userId, establishmentId, ct);
        if (gate is not null)
        {
            return Result.Failure(gate);
        }

        // Services of a deleted group stay, ungrouped.
        await db.Services.Where(s => s.EstablishmentId == establishmentId && s.GroupId == groupId)
            .ExecuteUpdateAsync(set => set.SetProperty(s => s.GroupId, (Guid?)null), ct);
        var removed = await db.ServiceGroups.Where(g => g.Id == groupId && g.EstablishmentId == establishmentId).ExecuteDeleteAsync(ct);
        return removed == 0 ? Result.Failure(GroupNotFound) : Result.Success();
    }

    public async Task<Result> ReorderGroupsAsync(Guid userId, Guid establishmentId, OrderInput input, CancellationToken ct)
    {
        var gate = await EditableAsync(userId, establishmentId, ct);
        if (gate is not null)
        {
            return Result.Failure(gate);
        }

        var groups = await db.ServiceGroups.Where(g => g.EstablishmentId == establishmentId).ToListAsync(ct);
        return await ApplyOrderAsync(groups, input.Ids, (g, i) => g.SortOrder = i, ct);
    }

    // ───────── Services ─────────

    public async Task<Result<Guid>> AddServiceAsync(Guid userId, Guid establishmentId, ServiceInput input, CancellationToken ct)
    {
        var gate = await EditableAsync(userId, establishmentId, ct);
        if (gate is not null)
        {
            return Result.Failure<Guid>(gate);
        }

        var invalid = await ValidateAsync(establishmentId, input, ct);
        if (invalid is not null)
        {
            return Result.Failure<Guid>(invalid);
        }

        var count = await db.Services.CountAsync(s => s.EstablishmentId == establishmentId && s.DeletedAt == null, ct);
        if (entitlements is not null && (await entitlements.EffectiveAsync(establishmentId, ct)).Features.MaxServices is { } planLimit && count >= planLimit)
        {
            return Result.Failure<Guid>(new Error("service.limit_reached",
                $"Votre formule actuelle limite le catalogue à {planLimit} prestation(s). Passez à une formule supérieure pour en ajouter davantage."));
        }

        if (count >= MaxServices)
        {
            return Result.Failure<Guid>(PartnerErrors.Invalid($"Au plus {MaxServices} prestations."));
        }

        var service = new Service
        {
            EstablishmentId = establishmentId,
            GroupId = input.GroupId,
            CategoryId = input.CategoryId,
            Name = input.Name.Trim(),
            Description = string.IsNullOrWhiteSpace(input.Description) ? null : input.Description.Trim(),
            Price = input.Price,
            PriceType = Enum.Parse<PriceType>(input.PriceType, ignoreCase: true),
            DurationMinutes = input.DurationMinutes,
            IsBookable = input.IsBookable,
            IsVisible = input.IsVisible,
            PromoPercent = input.PromoPercent,
            PromoEndsAt = input.PromoPercent > 0 ? input.PromoEndsAt : null,
            Perks = NormalizePerks(input.Perks),
            SortOrder = count,
        };
        db.Services.Add(service);
        await db.SaveChangesAsync(ct);
        return Result.Success(service.Id);
    }

    public async Task<Result> UpdateServiceAsync(Guid userId, Guid establishmentId, Guid serviceId, ServiceInput input, CancellationToken ct)
    {
        var gate = await EditableAsync(userId, establishmentId, ct);
        if (gate is not null)
        {
            return Result.Failure(gate);
        }

        var service = await db.Services.FirstOrDefaultAsync(s => s.Id == serviceId && s.EstablishmentId == establishmentId && s.DeletedAt == null, ct);
        if (service is null)
        {
            return Result.Failure(ServiceNotFound);
        }

        var invalid = await ValidateAsync(establishmentId, input, ct);
        if (invalid is not null)
        {
            return Result.Failure(invalid);
        }

        service.GroupId = input.GroupId;
        service.CategoryId = input.CategoryId;
        service.Name = input.Name.Trim();
        service.Description = string.IsNullOrWhiteSpace(input.Description) ? null : input.Description.Trim();
        service.Price = input.Price;
        service.PriceType = Enum.Parse<PriceType>(input.PriceType, ignoreCase: true);
        service.DurationMinutes = input.DurationMinutes;
        service.IsBookable = input.IsBookable;
        service.IsVisible = input.IsVisible;
        service.PromoPercent = input.PromoPercent;
        service.PromoEndsAt = input.PromoPercent > 0 ? input.PromoEndsAt : null;
        service.Perks = NormalizePerks(input.Perks);
        await db.SaveChangesAsync(ct);
        return Result.Success();
    }

    public async Task<Result> DeleteServiceAsync(Guid userId, Guid establishmentId, Guid serviceId, CancellationToken ct)
    {
        var gate = await EditableAsync(userId, establishmentId, ct);
        if (gate is not null)
        {
            return Result.Failure(gate);
        }

        var service = await db.Services.FirstOrDefaultAsync(s => s.Id == serviceId && s.EstablishmentId == establishmentId && s.DeletedAt == null, ct);
        if (service is null)
        {
            return Result.Failure(ServiceNotFound);
        }

        service.DeletedAt = time.GetUtcNow();
        await db.SaveChangesAsync(ct);
        return Result.Success();
    }

    public async Task<Result> ReorderServicesAsync(Guid userId, Guid establishmentId, OrderInput input, CancellationToken ct)
    {
        var gate = await EditableAsync(userId, establishmentId, ct);
        if (gate is not null)
        {
            return Result.Failure(gate);
        }

        var services = await db.Services.Where(s => s.EstablishmentId == establishmentId && s.DeletedAt == null).ToListAsync(ct);
        return await ApplyOrderAsync(services, input.Ids, (s, i) => s.SortOrder = i, ct);
    }

    // ───────── Helpers ─────────

    /// <summary>Null when the establishment belongs to the user and may be edited; otherwise the error to return.</summary>
    private async Task<Error?> EditableAsync(Guid userId, Guid establishmentId, CancellationToken ct)
    {
        var status = await PartnerSupport.OwnedBy(db, userId, establishmentId).Select(e => (EstablishmentStatus?)e.Status).FirstOrDefaultAsync(ct);
        return status switch
        {
            null => PartnerErrors.NotFound,
            { } s when !PartnerSupport.IsEditable(s) => PartnerErrors.Locked,
            _ => null,
        };
    }

    private async Task<Error?> ValidateAsync(Guid establishmentId, ServiceInput input, CancellationToken ct)
    {
        var name = input.Name?.Trim() ?? string.Empty;
        if (name.Length is < 2 or > 150)
        {
            return PartnerErrors.Invalid("Le nom de la prestation doit contenir entre 2 et 150 caractères.");
        }

        if (input.Description is { Length: > 1000 })
        {
            return PartnerErrors.Invalid("La description est limitée à 1000 caractères.");
        }

        if (input.Price is < 0 or > MaxPrice)
        {
            return PartnerErrors.Invalid("Le prix doit être compris entre 0 et 10 000 000.");
        }

        if (!Enum.TryParse<PriceType>(input.PriceType, ignoreCase: true, out var priceType) || !Enum.IsDefined(priceType))
        {
            return PartnerErrors.Invalid("Type de prix inconnu (Fixed ou From).");
        }

        if (input.DurationMinutes is < 5 or > 720)
        {
            return PartnerErrors.Invalid("La durée doit être comprise entre 5 et 720 minutes.");
        }

        if (input.PromoPercent is < 0 or > 90)
        {
            return PartnerErrors.Invalid("La remise promotionnelle doit être comprise entre 0 et 90 %.");
        }

        var perks = input.Perks?.Select(p => p?.Trim()).Where(p => !string.IsNullOrEmpty(p)).ToList() ?? [];
        if (perks.Count > 10 || perks.Any(p => p!.Length > 100))
        {
            return PartnerErrors.Invalid("Une formule peut inclure au plus 10 avantages de 100 caractères maximum chacun.");
        }

        if (!await db.EstablishmentCategories.AnyAsync(c => c.EstablishmentId == establishmentId && c.CategoryId == input.CategoryId, ct))
        {
            return PartnerErrors.Invalid("La catégorie doit être l'une de celles de l'établissement.");
        }

        if (input.GroupId is { } groupId && !await db.ServiceGroups.AnyAsync(g => g.Id == groupId && g.EstablishmentId == establishmentId, ct))
        {
            return PartnerErrors.Invalid("Rubrique inconnue.");
        }

        return null;
    }

    private static string? NormalizePerks(IReadOnlyList<string>? perks)
    {
        if (perks is null || perks.Count == 0)
        {
            return null;
        }

        var joined = string.Join('\n', perks.Select(p => p?.Trim()).Where(p => !string.IsNullOrEmpty(p)));
        return string.IsNullOrEmpty(joined) ? null : joined[..Math.Min(joined.Length, 600)];
    }

    private async Task<Result> ApplyOrderAsync<T>(List<T> items, IReadOnlyList<Guid> ids, Action<T, int> setOrder, CancellationToken ct)
        where T : Entity
    {
        var byId = items.ToDictionary(i => i.Id);
        if (ids is null || ids.Distinct().Count() != ids.Count || ids.Any(id => !byId.ContainsKey(id)))
        {
            return Result.Failure(PartnerErrors.Invalid("Liste d'identifiants invalide."));
        }

        var position = 0;
        foreach (var id in ids)
        {
            setOrder(byId[id], position++);
        }

        foreach (var rest in items.Where(i => !ids.Contains(i.Id)).OrderBy(i => i.Id))
        {
            setOrder(rest, position++);
        }

        await db.SaveChangesAsync(ct);
        return Result.Success();
    }
}
