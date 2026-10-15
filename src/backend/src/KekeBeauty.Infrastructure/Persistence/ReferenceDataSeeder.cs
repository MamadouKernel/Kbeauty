using KekeBeauty.Modules.Billing.Domain;
using KekeBeauty.Modules.Catalog.Domain;
using KekeBeauty.Modules.Geography.Domain;
using Microsoft.EntityFrameworkCore;

namespace KekeBeauty.Infrastructure.Persistence;

/// <summary>Idempotent seed of reference data (PRD §8.4): Côte d'Ivoire geography and beauty categories.</summary>
public static class ReferenceDataSeeder
{
    private static readonly string[] AbidjanCommunes =
    [
        "Abobo", "Adjamé", "Attécoubé", "Cocody", "Koumassi", "Marcory", "Plateau",
        "Port-Bouët", "Treichville", "Yopougon", "Anyama", "Bingerville", "Songon",
    ];

    private static readonly (string Name, string Slug, string Icon)[] Categories =
    [
        ("Salon de coiffure", "salon-de-coiffure", "hair"),
        ("Espace spa", "espace-spa", "spa"),
        ("Massage", "massage", "massage"),
        ("Onglerie", "onglerie", "nails"),
        ("Barbier", "barbier", "barber"),
        ("Esthétique / Soins visage", "esthetique-soins-visage", "skincare"),
        ("Maquillage", "maquillage", "makeup"),
    ];

    public const string FreePlanCode = "free";
    public const string PremiumPlanCode = "premium";

    public static async Task SeedAsync(KekeBeautyDbContext db, CancellationToken cancellationToken = default)
    {
        // Placeholder amounts (PRD §8.4): the admin replaces them; subscriptions keep the price they were sold at.
        if (!await db.Plans.AnyAsync(cancellationToken))
        {
            db.Plans.AddRange(
                new Plan
                {
                    Code = FreePlanCode,
                    Name = "Gratuit",
                    SortOrder = 1,
                    Features = new PlanFeatures { Booking = false, Videos = false, MaxMedia = 5, Featured = false, Stats = false },
                },
                new Plan
                {
                    Code = PremiumPlanCode,
                    Name = "Premium",
                    MonthlyPrice = 15000m,
                    AnnualPrice = 150000m,
                    SortOrder = 2,
                    Features = new PlanFeatures { Booking = true, Videos = true, MaxMedia = 30, Featured = true, Stats = true },
                });
        }

        if (!await db.Countries.AnyAsync(cancellationToken))
        {
            db.Countries.Add(BuildCoteDIvoire());
        }

        if (!await db.BeautyCategories.AnyAsync(cancellationToken))
        {
            db.BeautyCategories.AddRange(Categories.Select((c, index) => new BeautyCategory
            {
                Name = c.Name,
                Slug = c.Slug,
                Icon = c.Icon,
                SortOrder = index + 1,
            }));
        }

        await db.SaveChangesAsync(cancellationToken);
    }

    private static Country BuildCoteDIvoire()
    {
        var country = new Country { Iso2 = "CI", Name = "Côte d'Ivoire", PhonePrefix = "+225", CurrencyCode = "XOF" };

        var abidjan = new City { Name = "Abidjan" };
        abidjan.Communes.AddRange(AbidjanCommunes.Select(name => new Commune { Name = name }));
        country.Regions.Add(NewRegion("District autonome d'Abidjan", abidjan));

        country.Regions.Add(NewRegion("District autonome de Yamoussoukro", new City { Name = "Yamoussoukro" }));
        country.Regions.Add(NewRegion("Gbêkê", new City { Name = "Bouaké" }));
        country.Regions.Add(NewRegion("Poro", new City { Name = "Korhogo" }));
        country.Regions.Add(NewRegion("San-Pédro", new City { Name = "San-Pédro" }));
        country.Regions.Add(NewRegion("Haut-Sassandra", new City { Name = "Daloa" }));
        country.Regions.Add(NewRegion("Sud-Comoé", new City { Name = "Grand-Bassam" }));

        return country;
    }

    private static Region NewRegion(string name, City city)
    {
        var region = new Region { Name = name };
        region.Cities.Add(city);
        return region;
    }
}
