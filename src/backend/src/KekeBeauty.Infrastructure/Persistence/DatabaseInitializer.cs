using KekeBeauty.Modules.Identity.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace KekeBeauty.Infrastructure.Persistence;

public sealed class DatabaseOptions
{
    public const string SectionName = "Database";

    public bool MigrateOnStartup { get; init; }
    public bool SeedOnStartup { get; init; }

    /// <summary>Fictional establishments for development and demos. Must stay off in production.</summary>
    public bool SeedDemoData { get; init; }
}

/// <summary>
/// Applies migrations and seeds reference data when the host actually starts. Kept as a hosted
/// service (not code between Build() and Run()) so EF design-time tooling never needs a database.
/// </summary>
internal sealed class DatabaseInitializer(IServiceScopeFactory scopeFactory, IOptions<DatabaseOptions> options) : IHostedService
{
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        var settings = options.Value;
        if (!settings.MigrateOnStartup && !settings.SeedOnStartup)
        {
            return;
        }

        await using var scope = scopeFactory.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<KekeBeautyDbContext>();

        if (settings.MigrateOnStartup)
        {
            await db.Database.MigrateAsync(cancellationToken);
        }

        if (settings.SeedOnStartup)
        {
            await ReferenceDataSeeder.SeedAsync(db, cancellationToken);
            await KekeBeauty.Infrastructure.Admin.LegalDocumentService.SeedAsync(db, scope.ServiceProvider.GetRequiredService<TimeProvider>(), cancellationToken);
        }

        if (settings.SeedDemoData)
        {
            await DemoDataSeeder.SeedAsync(db, cancellationToken);
        }

        // No-op unless Admin:Email / Admin:Password are provided through configuration.
        await scope.ServiceProvider.GetRequiredService<SuperAdminSeeder>().SeedAsync(cancellationToken);
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
