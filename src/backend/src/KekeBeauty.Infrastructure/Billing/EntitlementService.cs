using KekeBeauty.Infrastructure.Persistence;
using KekeBeauty.Modules.Billing.Domain;
using KekeBeauty.Modules.Catalog.Domain;
using Microsoft.EntityFrameworkCore;

namespace KekeBeauty.Infrastructure.Billing;

/// <summary>What an establishment may use: the features of its active subscription, otherwise those of the free plan.</summary>
public sealed class EntitlementService(KekeBeautyDbContext db)
{
    public const string FreePlanCode = "free";

    public async Task<(string PlanCode, PlanFeatures Features)> EffectiveAsync(Guid establishmentId, CancellationToken ct)
    {
        var active = await (
            from s in db.Subscriptions.AsNoTracking()
            where s.EstablishmentId == establishmentId && (s.Status == SubscriptionStatus.Active || s.Status == SubscriptionStatus.Trialing)
            join p in db.Plans.AsNoTracking() on s.PlanId equals p.Id
            orderby s.StartDate descending
            select p)
            .FirstOrDefaultAsync(ct);
        if (active is not null)
        {
            return (active.Code, active.Features);
        }

        var free = await db.Plans.AsNoTracking().FirstOrDefaultAsync(p => p.Code == FreePlanCode, ct);
        return (FreePlanCode, free?.Features ?? PlanFeatures.FreeDefaults());
    }

    /// <summary>Brings the denormalised flags of the establishment in line with its plan (booking allowed, featured).</summary>
    public async Task SyncAsync(Guid establishmentId, CancellationToken ct)
    {
        var establishment = await db.Establishments.FirstOrDefaultAsync(e => e.Id == establishmentId, ct);
        if (establishment is null)
        {
            return;
        }

        var (_, features) = await EffectiveAsync(establishmentId, ct);
        Apply(establishment, features);
        await db.SaveChangesAsync(ct);
    }

    public static void Apply(Establishment establishment, PlanFeatures features)
    {
        establishment.BookingAllowed = features.Booking;
        establishment.IsFeatured = features.Featured;
    }
}
