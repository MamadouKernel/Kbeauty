using KekeBeauty.Modules.Billing.Domain;
using KekeBeauty.Modules.Catalog.Domain;
using KekeBeauty.Modules.Scheduling.Domain;
using Microsoft.EntityFrameworkCore;
using NetTopologySuite.Geometries;

namespace KekeBeauty.Infrastructure.Persistence;

/// <summary>
/// Fictional establishments in Abidjan for development and demos (PRD §8.4, <c>Database:SeedDemoData</c>).
/// Never enabled in production: every name, phone number and address below is invented.
/// </summary>
public static class DemoDataSeeder
{
    private sealed record ServiceSpec(string Group, string Category, string Name, decimal Price, PriceType Type, int Minutes);

    private sealed record Spec(
        string Name,
        string Slug,
        string Commune,
        double Latitude,
        double Longitude,
        string Address,
        string Phone,
        string[] Categories,
        bool Featured,
        bool Booking,
        string Hours,
        string Description,
        ServiceSpec[] Services);

    private static readonly Spec[] Specs =
    [
        new("Maison Éclat Coiffure", "maison-eclat-coiffure", "Cocody", 5.3599, -3.9877, "Rue des Jardins, Deux-Plateaux", "+2250700000001",
            ["salon-de-coiffure"], true, true, "weekdays",
            "Salon de coiffure afro et mixte : tresses, tissages, soins capillaires et coupes. Une équipe passionnée dans un cadre lumineux.",
            [
                new("Tresses", "salon-de-coiffure", "Box braids", 25000, PriceType.From, 240),
                new("Tresses", "salon-de-coiffure", "Cornrows", 12000, PriceType.From, 120),
                new("Tresses", "salon-de-coiffure", "Vanilles", 15000, PriceType.From, 150),
                new("Soins", "salon-de-coiffure", "Soin hydratant", 8000, PriceType.Fixed, 45),
                new("Soins", "salon-de-coiffure", "Brushing", 5000, PriceType.Fixed, 40),
            ]),
        new("Spa Lagune Bleue", "spa-lagune-bleue", "Marcory", 5.3000, -3.9833, "Boulevard de Marseille, Zone 4", "+2250700000002",
            ["espace-spa", "massage"], true, true, "daily",
            "Un havre de calme en bord de lagune : hammam, gommages, massages et rituels de beauté pour se ressourcer.",
            [
                new("Massages", "massage", "Massage relaxant 60 min", 30000, PriceType.Fixed, 60),
                new("Massages", "massage", "Massage aux pierres chaudes", 40000, PriceType.Fixed, 90),
                new("Rituels", "espace-spa", "Hammam et gommage", 25000, PriceType.Fixed, 75),
                new("Rituels", "espace-spa", "Rituel visage éclat", 35000, PriceType.Fixed, 60),
            ]),
        new("Onglerie Perle d'Ivoire", "onglerie-perle-d-ivoire", "Plateau", 5.3235, -4.0197, "Avenue Chardy, Immeuble Les Arcades", "+2250700000003",
            ["onglerie"], false, false, "weekdays",
            "Manucure, pose de gel et nail art dans le centre des affaires. Résultats soignés et durables.",
            [
                new("Mains", "onglerie", "Manucure classique", 8000, PriceType.Fixed, 40),
                new("Mains", "onglerie", "Pose gel", 18000, PriceType.From, 90),
                new("Pieds", "onglerie", "Pédicure complète", 15000, PriceType.Fixed, 60),
                new("Création", "onglerie", "Nail art (par ongle)", 1000, PriceType.From, 10),
            ]),
        new("Barbier Royal 225", "barbier-royal-225", "Yopougon", 5.3364, -4.0897, "Carrefour Siporex", "+2250700000004",
            ["barbier"], false, false, "daily",
            "Coupes nettes, dégradés et soins de la barbe par des barbiers expérimentés. Ambiance conviviale.",
            [
                new("Coupes", "barbier", "Coupe homme", 3000, PriceType.Fixed, 30),
                new("Coupes", "barbier", "Dégradé", 4000, PriceType.Fixed, 40),
                new("Barbe", "barbier", "Taille et soin de la barbe", 3000, PriceType.Fixed, 25),
            ]),
        new("Institut Teint Doré", "institut-teint-dore", "Cocody", 5.3480, -3.9950, "Rue des Orchidées, Riviera 2", "+2250700000005",
            ["esthetique-soins-visage", "maquillage"], false, true, "weekdays",
            "Soins du visage, épilation et maquillage de soirée. Produits adaptés aux peaux foncées.",
            [
                new("Visage", "esthetique-soins-visage", "Nettoyage de peau", 20000, PriceType.Fixed, 60),
                new("Visage", "esthetique-soins-visage", "Soin anti-taches", 30000, PriceType.Fixed, 75),
                new("Maquillage", "maquillage", "Maquillage de soirée", 15000, PriceType.Fixed, 45),
            ]),
        new("Studio Tresses d'Afrique", "studio-tresses-d-afrique", "Abobo", 5.4167, -4.0167, "Abobo Gare, près du marché", "+2250700000006",
            ["salon-de-coiffure"], false, true, "daily",
            "Spécialiste des tresses africaines et des coiffures de cérémonie, à prix doux.",
            [
                new("Tresses", "salon-de-coiffure", "Tresses collées", 6000, PriceType.From, 90),
                new("Tresses", "salon-de-coiffure", "Nattes avec rajouts", 10000, PriceType.From, 150),
                new("Cérémonie", "salon-de-coiffure", "Coiffure de mariée", 40000, PriceType.From, 180),
            ]),
        new("Zen Palmier Massage", "zen-palmier-massage", "Treichville", 5.2900, -4.0100, "Rue 12, Zone 3", "+2250700000007",
            ["massage", "espace-spa"], false, false, "daily",
            "Massages de bien-être et de récupération dans une ambiance apaisante.",
            [
                new("Massages", "massage", "Massage dos et nuque", 15000, PriceType.Fixed, 30),
                new("Massages", "massage", "Massage corps complet", 28000, PriceType.Fixed, 60),
                new("Massages", "massage", "Massage californien", 32000, PriceType.Fixed, 75),
            ]),
        new("Glam Atelier Maquillage", "glam-atelier-maquillage", "Marcory", 5.3050, -3.9780, "Rue du Canal, Marcory Résidentiel", "+2250700000008",
            ["maquillage", "esthetique-soins-visage"], false, false, "weekdays",
            "Maquilleuse professionnelle : mariage, shooting, événements et cours d'auto-maquillage.",
            [
                new("Maquillage", "maquillage", "Maquillage de jour", 10000, PriceType.Fixed, 40),
                new("Maquillage", "maquillage", "Maquillage de mariée (essai inclus)", 60000, PriceType.From, 120),
                new("Cours", "maquillage", "Cours d'auto-maquillage", 25000, PriceType.Fixed, 90),
            ]),
        new("Nail Bar Cocody Riviera", "nail-bar-cocody-riviera", "Cocody", 5.3700, -3.9700, "Boulevard Latrille, Riviera Palmeraie", "+2250700000009",
            ["onglerie"], true, true, "daily",
            "Un bar à ongles moderne : semi-permanent, gel, capsules et décorations tendance. Sur rendez-vous ou sans.",
            [
                new("Mains", "onglerie", "Semi-permanent", 12000, PriceType.Fixed, 60),
                new("Mains", "onglerie", "Pose capsules", 20000, PriceType.From, 100),
                new("Pieds", "onglerie", "Beauté des pieds", 14000, PriceType.Fixed, 60),
            ]),
        new("Coiffure & Spa Ébène", "coiffure-et-spa-ebene", "Bingerville", 5.3550, -3.8850, "Route de la Baie, Bingerville", "+2250700000010",
            ["salon-de-coiffure", "espace-spa"], false, false, "weekdays",
            "Coiffure et soins du corps réunis dans une maison de caractère à Bingerville.",
            [
                new("Coiffure", "salon-de-coiffure", "Coupe et brushing", 7000, PriceType.Fixed, 60),
                new("Coiffure", "salon-de-coiffure", "Tissage", 30000, PriceType.From, 180),
                new("Spa", "espace-spa", "Gommage corps", 18000, PriceType.Fixed, 45),
            ]),
    ];

    public static async Task SeedAsync(KekeBeautyDbContext db, CancellationToken cancellationToken = default)
    {
        if (await db.Establishments.AnyAsync(cancellationToken))
        {
            await EnsureSubscriptionsAsync(db, cancellationToken);
            return;
        }

        var categories = await db.BeautyCategories.ToDictionaryAsync(c => c.Slug, cancellationToken);
        var communes = await db.Communes.ToDictionaryAsync(c => c.Name, c => c.Id, cancellationToken);
        if (categories.Count == 0 || communes.Count == 0)
        {
            return; // reference data must be seeded first
        }

        var premium = await db.Plans.AsNoTracking().FirstOrDefaultAsync(p => p.Code == ReferenceDataSeeder.PremiumPlanCode, cancellationToken);
        var now = DateTimeOffset.UtcNow;
        foreach (var spec in Specs)
        {
            var establishment = new Establishment
            {
                Name = spec.Name,
                Slug = spec.Slug,
                Description = spec.Description,
                CustomerServicePhone = spec.Phone,
                CommuneId = communes[spec.Commune],
                AddressLine = spec.Address,
                Location = new Point(spec.Longitude, spec.Latitude) { SRID = 4326 },
                Status = EstablishmentStatus.Active,
                IsFeatured = spec.Featured,
                BookingEnabled = spec.Booking,
                BookingAllowed = spec.Booking && premium is not null,
            };
            db.Establishments.Add(establishment);

            if (premium is not null && (spec.Booking || spec.Featured))
            {
                // Demo establishments have no owner, hence no invoices: the plan simply stays active.
                db.Subscriptions.Add(new Subscription
                {
                    EstablishmentId = establishment.Id,
                    PlanId = premium.Id,
                    Cycle = BillingCycle.Monthly,
                    UnitPriceSnapshot = premium.MonthlyPrice,
                    CurrencyCode = premium.CurrencyCode,
                    Status = SubscriptionStatus.Active,
                    StartDate = now,
                    CreatedAt = now,
                });
            }

            foreach (var slug in spec.Categories)
            {
                establishment.Categories.Add(new EstablishmentCategory { CategoryId = categories[slug].Id });
            }

            AddMedia(db, establishment, categories[spec.Categories[0]].Icon);
            AddServices(db, establishment.Id, spec, categories);
            AddHours(db, establishment.Id, spec.Hours);
            db.Resources.Add(new Resource { EstablishmentId = establishment.Id, Name = "Principal" });
        }

        await db.SaveChangesAsync(cancellationToken);
    }

    /// <summary>
    /// Demo databases created before plans existed have bookable establishments without a subscription: give them the active Premium
    /// plan they would get today, so the demo keeps working and the finance dashboard has something to show. Idempotent.
    /// </summary>
    private static async Task EnsureSubscriptionsAsync(KekeBeautyDbContext db, CancellationToken cancellationToken)
    {
        var premium = await db.Plans.AsNoTracking().FirstOrDefaultAsync(p => p.Code == ReferenceDataSeeder.PremiumPlanCode, cancellationToken);
        if (premium is null)
        {
            return;
        }

        var now = DateTimeOffset.UtcNow;
        var demo = await db.Establishments
            .Where(e => e.OwnerUserId == null && e.BookingEnabled
                && !db.Subscriptions.Any(s => s.EstablishmentId == e.Id && s.Status == SubscriptionStatus.Active))
            .ToListAsync(cancellationToken);
        foreach (var establishment in demo)
        {
            establishment.BookingAllowed = true;
            db.Subscriptions.Add(new Subscription
            {
                EstablishmentId = establishment.Id,
                PlanId = premium.Id,
                Cycle = BillingCycle.Monthly,
                UnitPriceSnapshot = premium.MonthlyPrice,
                CurrencyCode = premium.CurrencyCode,
                Status = SubscriptionStatus.Active,
                StartDate = now,
                CreatedAt = now,
            });
        }

        if (demo.Count > 0)
        {
            await db.SaveChangesAsync(cancellationToken);
        }
    }

    private static void AddMedia(KekeBeautyDbContext db, Establishment establishment, string iconKey)
    {
        for (var i = 1; i <= 3; i++)
        {
            db.MediaItems.Add(new Media
            {
                EstablishmentId = establishment.Id,
                Kind = MediaKind.Photo,
                StorageKey = $"demo/{iconKey}-{i}.svg",
                AltText = $"{establishment.Name} — visuel {i}",
                Width = 1200,
                Height = 800,
                SortOrder = i,
                IsCover = i == 1,
            });
        }
    }

    private static void AddServices(KekeBeautyDbContext db, Guid establishmentId, Spec spec, Dictionary<string, BeautyCategory> categories)
    {
        var groups = new Dictionary<string, ServiceGroup>();
        var order = 0;
        foreach (var service in spec.Services)
        {
            if (!groups.TryGetValue(service.Group, out var group))
            {
                group = new ServiceGroup { EstablishmentId = establishmentId, Name = service.Group, SortOrder = groups.Count + 1 };
                groups[service.Group] = group;
                db.ServiceGroups.Add(group);
            }

            db.Services.Add(new Service
            {
                EstablishmentId = establishmentId,
                GroupId = group.Id,
                CategoryId = categories[service.Category].Id,
                Name = service.Name,
                Price = service.Price,
                PriceType = service.Type,
                DurationMinutes = service.Minutes,
                SortOrder = ++order,
            });
        }
    }

    private static void AddHours(KekeBeautyDbContext db, Guid establishmentId, string pattern)
    {
        // "weekdays": Mon–Sat 08:30–19:00 with a lunch break on weekdays; "daily": every day 09:00–20:00.
        DayOfWeek[] days = pattern == "daily"
            ? [DayOfWeek.Monday, DayOfWeek.Tuesday, DayOfWeek.Wednesday, DayOfWeek.Thursday, DayOfWeek.Friday, DayOfWeek.Saturday, DayOfWeek.Sunday]
            : [DayOfWeek.Monday, DayOfWeek.Tuesday, DayOfWeek.Wednesday, DayOfWeek.Thursday, DayOfWeek.Friday, DayOfWeek.Saturday];

        foreach (var day in days)
        {
            if (pattern == "daily")
            {
                db.OpeningHours.Add(Hour(establishmentId, day, 9, 0, 20, 0));
            }
            else if (day == DayOfWeek.Saturday)
            {
                db.OpeningHours.Add(Hour(establishmentId, day, 8, 30, 18, 0));
            }
            else
            {
                db.OpeningHours.Add(Hour(establishmentId, day, 8, 30, 12, 30));
                db.OpeningHours.Add(Hour(establishmentId, day, 14, 0, 19, 0));
            }
        }
    }

    private static OpeningHour Hour(Guid establishmentId, DayOfWeek day, int openH, int openM, int closeH, int closeM) => new()
    {
        EstablishmentId = establishmentId,
        DayOfWeek = day,
        OpensAt = new TimeOnly(openH, openM),
        ClosesAt = new TimeOnly(closeH, closeM),
    };
}
