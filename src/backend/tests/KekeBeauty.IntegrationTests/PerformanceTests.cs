using System.Diagnostics;
using System.Globalization;
using FluentAssertions;
using Npgsql;
using Xunit.Abstractions;

namespace KekeBeauty.IntegrationTests;

/// <summary>
/// Latency budgets of PRD section 11 on a realistic catalogue (50 000 establishments), measured in-process, i.e. without the network.
/// Opt-in because it starts its own PostgreSQL container and takes a few minutes: <c>KEKE_PERF=1 dotnet test --filter PerformanceTests</c>.
/// </summary>
public sealed class PerformanceTests(ITestOutputHelper output)
{
    private const int Establishments = 50_000;
    private const double SearchBudgetMs = 300;
    private const double AvailabilityBudgetMs = 200;

    private static readonly string SeedSql = $"""
        WITH communes AS (SELECT array_agg(id ORDER BY id) AS ids FROM geography.communes)
        INSERT INTO catalog.establishments
            (id, name, slug, description, customer_service_phone, commune_id, address_line, location, timezone, status,
             is_featured, booking_enabled, created_at, booking_allowed)
        SELECT gen_random_uuid(),
               'Salon ' || n || ' ' || (ARRAY['Éclat','Ébène','Lumière','Soleil','Perle','Karité'])[1 + n % 6],
               'perf-' || n,
               'Établissement synthétique de test de charge numéro ' || n,
               '+22507' || lpad((n % 100000000)::text, 8, '0'),
               communes.ids[1 + n % array_length(communes.ids, 1)],
               'Rue ' || n,
               ST_SetSRID(ST_MakePoint(-4.15 + random() * 0.3, 5.25 + random() * 0.2), 4326)::geography,
               'Africa/Abidjan', 'Active', (n % 50 = 0), (n % 3 = 0), now(), (n % 3 = 0)
        FROM generate_series(1, {Establishments}) AS n, communes;

        WITH cats AS (SELECT array_agg(id ORDER BY id) AS ids FROM catalog.beauty_categories)
        INSERT INTO catalog.establishment_categories (establishment_id, category_id)
        SELECT e.id, cats.ids[1 + substring(e.slug FROM 6)::int % array_length(cats.ids, 1)]
        FROM catalog.establishments e, cats WHERE e.slug LIKE 'perf-%';

        WITH cats AS (SELECT array_agg(id ORDER BY id) AS ids FROM catalog.beauty_categories)
        INSERT INTO catalog.establishment_categories (establishment_id, category_id)
        SELECT e.id, cats.ids[1 + (substring(e.slug FROM 6)::int + 3) % array_length(cats.ids, 1)]
        FROM catalog.establishments e, cats WHERE e.slug LIKE 'perf-%' AND substring(e.slug FROM 6)::int % 2 = 0;

        INSERT INTO catalog.media (id, establishment_id, kind, storage_key, thumb_key, alt_text, width, height, sort_order, is_cover)
        SELECT gen_random_uuid(), id, 'Photo', 'demo/hair-1.svg', NULL, 'Photo', 1200, 800, 1, true
        FROM catalog.establishments WHERE slug LIKE 'perf-%';

        INSERT INTO catalog.services (id, establishment_id, category_id, name, price, price_type, currency_code, duration_minutes, is_bookable, is_visible, sort_order)
        SELECT gen_random_uuid(), ec.establishment_id, ec.category_id, 'Prestation ' || s, 5000 + s * 2500, 'Fixed', 'XOF', 30 * s, true, true, s
        FROM catalog.establishment_categories ec
        JOIN catalog.establishments e ON e.id = ec.establishment_id AND e.slug LIKE 'perf-%'
        CROSS JOIN generate_series(1, 3) AS s;

        ANALYZE;
        """;

    private sealed record Stats(string Name, double P50, double P95, double P99);

    [Fact]
    public async Task Search_and_availability_stay_within_their_budgets_on_fifty_thousand_establishments()
    {
        if (Environment.GetEnvironmentVariable("KEKE_PERF") != "1")
        {
            output.WriteLine("Not run: set KEKE_PERF=1 to measure (starts its own PostgreSQL container, takes a few minutes).");
            return;
        }

        var api = new ApiFixture();
        await ((IAsyncLifetime)api).InitializeAsync();
        try
        {
            await using var connection = new NpgsqlConnection(api.ConnectionString);
            await connection.OpenAsync();
            await using (var seed = new NpgsqlCommand(SeedSql, connection) { CommandTimeout = 900 })
            {
                var watch = Stopwatch.StartNew();
                await seed.ExecuteNonQueryAsync();
                output.WriteLine($"Seeded {Establishments} establishments in {watch.Elapsed.TotalSeconds:0.0}s");
            }

            var communeId = (Guid)(await new NpgsqlCommand("SELECT id FROM geography.communes ORDER BY id LIMIT 1", connection).ExecuteScalarAsync())!;
            var demo = await new NpgsqlCommand("SELECT id FROM catalog.establishments WHERE slug = 'maison-eclat-coiffure'", connection).ExecuteScalarAsync();
            var demoId = (Guid)demo!;
            var serviceId = (Guid)(await new NpgsqlCommand($"SELECT id FROM catalog.services WHERE establishment_id = '{demoId}' ORDER BY sort_order LIMIT 1", connection).ExecuteScalarAsync())!;
            var day = TestSalons.NextOpenDay(api).UtcDateTime;
            var from = DateOnly.FromDateTime(day).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
            var to = DateOnly.FromDateTime(day.AddDays(7)).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

            var searches = new (string Name, string Url)[]
            {
                ("liste par défaut", "/api/v1/establishments?pageSize=20"),
                ("par catégorie", "/api/v1/establishments?category=salon-de-coiffure&pageSize=20"),
                ("par commune", $"/api/v1/establishments?communeId={communeId}&pageSize=20"),
                ("texte « eclat »", "/api/v1/establishments?q=eclat&pageSize=20"),
                ("à proximité (5 km)", "/api/v1/establishments?lat=5.35&lng=-4.0&radiusKm=5&sort=distance&pageSize=20"),
                ("réservables", "/api/v1/establishments?bookable=true&pageSize=20"),
            };

            using var client = api.CreateClient();
            var results = new List<Stats>();
            foreach (var (name, url) in searches)
            {
                results.Add(await MeasureAsync(client, name, url));
            }

            var availability = await MeasureAsync(client, "disponibilités (7 jours)", $"/api/v1/establishments/{demoId}/availability?from={from}&to={to}&serviceIds={serviceId}");
            var concurrent = await MeasureConcurrentAsync(api, "liste par défaut, 8 clients en parallèle", "/api/v1/establishments?pageSize=20");

            output.WriteLine($"{"Requête",-44} {"p50 ms",8} {"p95 ms",8} {"p99 ms",8}");
            foreach (var s in results.Append(availability).Append(concurrent))
            {
                output.WriteLine($"{s.Name,-44} {s.P50,8:0.0} {s.P95,8:0.0} {s.P99,8:0.0}");
            }

            foreach (var s in results)
            {
                s.P95.Should().BeLessThan(SearchBudgetMs, $"search '{s.Name}' budget (PRD section 11)");
            }

            availability.P95.Should().BeLessThan(AvailabilityBudgetMs, "availability budget (PRD section 11)");
        }
        finally
        {
            await ((IAsyncLifetime)api).DisposeAsync();
        }
    }

    private static async Task<Stats> MeasureAsync(HttpClient client, string name, string url, int count = 200)
    {
        for (var i = 0; i < 20; i++)
        {
            (await client.GetAsync(url)).EnsureSuccessStatusCode();
        }

        var times = new List<double>(count);
        for (var i = 0; i < count; i++)
        {
            var watch = Stopwatch.StartNew();
            var response = await client.GetAsync(url);
            await response.Content.ReadAsByteArrayAsync();
            times.Add(watch.Elapsed.TotalMilliseconds);
            response.EnsureSuccessStatusCode();
        }

        return Summarise(name, times);
    }

    private static async Task<Stats> MeasureConcurrentAsync(ApiFixture api, string name, string url, int clients = 8, int perClient = 40)
    {
        var all = new List<double>();
        var tasks = Enumerable.Range(0, clients).Select(async _ =>
        {
            using var client = api.CreateClient();
            var local = new List<double>(perClient);
            for (var i = 0; i < perClient; i++)
            {
                var watch = Stopwatch.StartNew();
                var response = await client.GetAsync(url);
                await response.Content.ReadAsByteArrayAsync();
                local.Add(watch.Elapsed.TotalMilliseconds);
                response.EnsureSuccessStatusCode();
            }

            lock (all)
            {
                all.AddRange(local);
            }
        });
        await Task.WhenAll(tasks);
        return Summarise(name, all);
    }

    private static Stats Summarise(string name, List<double> times)
    {
        times.Sort();
        double At(double q) => times[Math.Min((int)Math.Ceiling(q * times.Count) - 1, times.Count - 1)];
        return new Stats(name, At(0.5), At(0.95), At(0.99));
    }
}
