using System.Security.Cryptography;
using KekeBeauty.Infrastructure;
using Npgsql;

namespace KekeBeauty.Api.BackgroundJobs;

public sealed class DatabaseMigrationHostedService(IServiceProvider services, IWebHostEnvironment environment, ILogger<DatabaseMigrationHostedService> logger) : IHostedService
{
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        var directory = ResolveMigrationsDirectory();
        using var scope = services.CreateScope();
        var factory = scope.ServiceProvider.GetRequiredService<IDbConnectionFactory>();
        await using var connection = factory.CreateConnection();
        await connection.OpenAsync(cancellationToken);
        await using (var create = new NpgsqlCommand("CREATE TABLE IF NOT EXISTS schema_migrations (version VARCHAR(20) PRIMARY KEY, description TEXT NOT NULL, checksum VARCHAR(64) NOT NULL, applied_at TIMESTAMPTZ NOT NULL DEFAULT now())", connection))
            await create.ExecuteNonQueryAsync(cancellationToken);
        foreach (var file in Directory.GetFiles(directory, "*.sql").OrderBy(Path.GetFileName, StringComparer.Ordinal))
        {
            var name = Path.GetFileName(file);
            var separator = name.IndexOf('_');
            if (separator <= 0) throw new InvalidOperationException($"Nom de migration invalide: {name}");
            var version = name[..separator];
            var description = name[(separator + 1)..];
            var bytes = await File.ReadAllBytesAsync(file, cancellationToken);
            var checksum = Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
            await using var lookup = new NpgsqlCommand("SELECT checksum FROM schema_migrations WHERE version=@version", connection);
            lookup.Parameters.AddWithValue("version", version);
            var existing = await lookup.ExecuteScalarAsync(cancellationToken) as string;
            if (existing is not null)
            {
                if (!string.Equals(existing, checksum, StringComparison.OrdinalIgnoreCase))
                    throw new InvalidOperationException($"La migration {version} a été modifiée après application.");
                continue;
            }
            await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
            try
            {
                await using var migrate = new NpgsqlCommand(System.Text.Encoding.UTF8.GetString(bytes), connection, transaction) { CommandTimeout = 120 };
                await migrate.ExecuteNonQueryAsync(cancellationToken);
                await using var record = new NpgsqlCommand("INSERT INTO schema_migrations(version,description,checksum) VALUES(@version,@description,@checksum)", connection, transaction);
                record.Parameters.AddWithValue("version", version);
                record.Parameters.AddWithValue("description", description);
                record.Parameters.AddWithValue("checksum", checksum);
                await record.ExecuteNonQueryAsync(cancellationToken);
                await transaction.CommitAsync(cancellationToken);
                logger.LogInformation("Migration {Version} appliquée.", version);
            }
            catch
            {
                await transaction.RollbackAsync(cancellationToken);
                throw;
            }
        }
    }
    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    /// <summary>
    /// Le .csproj copie db/migrations/*.sql vers bin/.../migrations au build (CopyToOutputDirectory),
    /// donc AppContext.BaseDirectory/migrations est le bon dossier en Docker/publish comme en debug
    /// Visual Studio. `dotnet run` execute en revanche avec ContentRootPath = dossier source du
    /// projet (pas le dossier de sortie), ou ce copy n'existe pas : on retombe alors sur la source
    /// de verite db/migrations en remontant l'arborescence depuis ContentRootPath.
    /// </summary>
    private string ResolveMigrationsDirectory()
    {
        var outputDirectory = Path.Combine(AppContext.BaseDirectory, "migrations");
        if (Directory.Exists(outputDirectory)) return outputDirectory;

        var current = new DirectoryInfo(environment.ContentRootPath);
        while (current is not null)
        {
            var candidate = Path.Combine(current.FullName, "db", "migrations");
            if (Directory.Exists(candidate)) return candidate;
            current = current.Parent;
        }

        throw new DirectoryNotFoundException(
            $"Migrations absentes : ni {outputDirectory}, ni un dossier db/migrations trouve en remontant depuis {environment.ContentRootPath}.");
    }
}