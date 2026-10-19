using System.Diagnostics;
using KekeBeauty.Infrastructure.Admin;
using KekeBeauty.Infrastructure.Persistence;
using KekeBeauty.Modules.Administration.Domain;
using KekeBeauty.SharedKernel;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Npgsql;

namespace KekeBeauty.Infrastructure.Backups;

public sealed record BackupDto(
    Guid Id,
    string Kind,
    string Status,
    long SizeBytes,
    string? Error,
    DateTimeOffset StartedAt,
    DateTimeOffset? CompletedAt,
    Guid? CreatedBy);

/// <summary>Runs <c>pg_dump</c> in custom compressed format against the configured PostgreSQL database.</summary>
public sealed class PgDumpDatabaseDumper(IConfiguration configuration, ILogger<PgDumpDatabaseDumper> logger) : IDatabaseDumper
{
    public async Task<Result> DumpAsync(string destinationPath, CancellationToken cancellationToken = default)
    {
        var cs = configuration.GetConnectionString(DependencyInjection.ConnectionStringName);
        if (string.IsNullOrWhiteSpace(cs))
        {
            return Result.Failure(new Error("backup.dump_failed", "pg_dump a échoué : chaîne de connexion absente."));
        }

        var builder = new NpgsqlConnectionStringBuilder(cs);
        var exe = configuration["Backups:PgDumpPath"];
        if (string.IsNullOrWhiteSpace(exe))
        {
            exe = "pg_dump";
        }

        var timeoutMinutes = Math.Clamp(configuration.GetValue("Backups:TimeoutMinutes", 5), 1, 60);
        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutCts.CancelAfter(TimeSpan.FromMinutes(timeoutMinutes));

        var psi = new ProcessStartInfo(exe)
        {
            RedirectStandardError = true,
            RedirectStandardOutput = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        psi.ArgumentList.Add("--format=custom");
        psi.ArgumentList.Add("--no-owner");
        psi.ArgumentList.Add($"--file={destinationPath}");
        if (!string.IsNullOrWhiteSpace(builder.Host))
        {
            psi.ArgumentList.Add($"--host={builder.Host}");
        }

        if (builder.Port > 0)
        {
            psi.ArgumentList.Add($"--port={builder.Port}");
        }

        if (!string.IsNullOrWhiteSpace(builder.Username))
        {
            psi.ArgumentList.Add($"--username={builder.Username}");
        }

        if (!string.IsNullOrWhiteSpace(builder.Database))
        {
            psi.ArgumentList.Add($"--dbname={builder.Database}");
        }

        if (!string.IsNullOrEmpty(builder.Password))
        {
            psi.Environment["PGPASSWORD"] = builder.Password;
        }

        try
        {
            using var proc = Process.Start(psi);
            if (proc is null)
            {
                return Result.Failure(new Error("backup.dump_failed", "Impossible de démarrer pg_dump."));
            }

            var stderr = await proc.StandardError.ReadToEndAsync(timeoutCts.Token);
            await proc.WaitForExitAsync(timeoutCts.Token);
            if (proc.ExitCode != 0)
            {
                logger.LogError("pg_dump exited with code {Code}: {Error}", proc.ExitCode, stderr);
                return Result.Failure(new Error("backup.dump_failed", "pg_dump a échoué ; consultez les journaux du serveur."));
            }

            return Result.Success();
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "pg_dump invocation failed.");
            return Result.Failure(new Error("backup.dump_failed", "pg_dump a échoué ; consultez les journaux du serveur."));
        }
    }
}

/// <summary>Creates, lists, streams, deletes and purges database dumps stored in <see cref="StorageBuckets.PrivateBackups"/>.</summary>
public sealed class BackupService(
    KekeBeautyDbContext db,
    IDatabaseDumper dumper,
    IFileStorage storage,
    IConfiguration configuration,
    TimeProvider time)
{
    public async Task<IReadOnlyList<BackupDto>> ListAsync(CancellationToken ct)
    {
        var rows = await db.DatabaseBackups.AsNoTracking()
            .OrderByDescending(b => b.StartedAt)
            .Take(100)
            .ToListAsync(ct);
        return rows.Select(ToDto).ToList();
    }

    public async Task<Result<BackupDto>> CreateAsync(AdminContext? admin, BackupKind kind, CancellationToken ct)
    {
        var now = time.GetUtcNow();
        var backup = new DatabaseBackup
        {
            Kind = kind,
            Status = BackupStatus.Running,
            StartedAt = now,
            CreatedBy = admin?.ActorId,
        };
        db.DatabaseBackups.Add(backup);
        await db.SaveChangesAsync(ct);

        var tempFile = Path.Combine(Path.GetTempPath(), $"keke-backup-{backup.Id:N}.dump");
        try
        {
            var dump = await dumper.DumpAsync(tempFile, ct);
            if (dump.IsFailure)
            {
                backup.Status = BackupStatus.Failed;
                backup.Error = dump.Error.Message;
                backup.CompletedAt = time.GetUtcNow();
                if (admin is not null)
                {
                    Audit.Add(db, admin, time, "backup.create", "DatabaseBackup", backup.Id, new { status = "Failed" });
                }

                await db.SaveChangesAsync(ct);
                return Result.Success(ToDto(backup));
            }

            var info = new FileInfo(tempFile);
            var key = $"{backup.Id:N}.dump";
            await using (var stream = File.OpenRead(tempFile))
            {
                await storage.PutAsync(StorageBuckets.PrivateBackups, key, stream, "application/octet-stream", ct);
            }

            backup.Status = BackupStatus.Succeeded;
            backup.FileKey = key;
            backup.SizeBytes = info.Length;
            backup.CompletedAt = time.GetUtcNow();
            if (admin is not null)
            {
                Audit.Add(db, admin, time, "backup.create", "DatabaseBackup", backup.Id, new { sizeBytes = backup.SizeBytes });
            }

            await db.SaveChangesAsync(ct);
            return Result.Success(ToDto(backup));
        }
        finally
        {
            if (File.Exists(tempFile))
            {
                try
                {
                    File.Delete(tempFile);
                }
                catch
                {
                    // Best-effort temp cleanup.
                }
            }
        }
    }

    public async Task<Result<(StoredFile File, string FileName)>> OpenAsync(AdminContext admin, Guid id, CancellationToken ct)
    {
        var backup = await db.DatabaseBackups.AsNoTracking().FirstOrDefaultAsync(b => b.Id == id, ct);
        if (backup is null)
        {
            return Result.Failure<(StoredFile, string)>(new Error("backup.not_found", "Sauvegarde introuvable."));
        }

        if (backup.Status != BackupStatus.Succeeded || string.IsNullOrEmpty(backup.FileKey))
        {
            return Result.Failure<(StoredFile, string)>(new Error("backup.not_ready", "Cette sauvegarde n'est pas disponible au téléchargement."));
        }

        var file = await storage.OpenReadAsync(StorageBuckets.PrivateBackups, backup.FileKey, ct);
        if (file is null)
        {
            return Result.Failure<(StoredFile, string)>(new Error("backup.not_found", "Fichier de sauvegarde introuvable."));
        }

        Audit.Add(db, admin, time, "backup.download", "DatabaseBackup", backup.Id);
        await db.SaveChangesAsync(ct);
        var fileName = $"keke-beauty-{backup.StartedAt:yyyyMMdd-HHmmss}.dump";
        return Result.Success((file, fileName));
    }

    public async Task<Result> DeleteAsync(AdminContext admin, Guid id, CancellationToken ct)
    {
        var backup = await db.DatabaseBackups.FirstOrDefaultAsync(b => b.Id == id, ct);
        if (backup is null)
        {
            return Result.Failure(new Error("backup.not_found", "Sauvegarde introuvable."));
        }

        if (backup.Status == BackupStatus.Running)
        {
            return Result.Failure(new Error("backup.not_ready", "Impossible de supprimer une sauvegarde en cours."));
        }

        if (!string.IsNullOrEmpty(backup.FileKey))
        {
            await storage.DeleteAsync(StorageBuckets.PrivateBackups, backup.FileKey, ct);
        }

        db.DatabaseBackups.Remove(backup);
        Audit.Add(db, admin, time, "backup.delete", "DatabaseBackup", id);
        await db.SaveChangesAsync(ct);
        return Result.Success();
    }

    /// <summary>Purges expired backups while always keeping the three newest successful dumps.</summary>
    public async Task<int> PurgeAsync(CancellationToken ct)
    {
        var retentionDays = Math.Max(1, configuration.GetValue("Backups:RetentionDays", 14));
        var cutoff = time.GetUtcNow().AddDays(-retentionDays);

        var all = await db.DatabaseBackups.OrderByDescending(b => b.StartedAt).ToListAsync(ct);
        var keepIds = all.Where(b => b.Status == BackupStatus.Succeeded).Take(3).Select(b => b.Id).ToHashSet();
        var expired = all.Where(b => b.Status != BackupStatus.Running && b.StartedAt < cutoff && !keepIds.Contains(b.Id)).ToList();
        if (expired.Count == 0)
        {
            return 0;
        }

        foreach (var b in expired)
        {
            if (!string.IsNullOrEmpty(b.FileKey))
            {
                await storage.DeleteAsync(StorageBuckets.PrivateBackups, b.FileKey, ct);
            }

            db.DatabaseBackups.Remove(b);
        }

        await db.SaveChangesAsync(ct);
        return expired.Count;
    }

    private static BackupDto ToDto(DatabaseBackup b) => new(
        b.Id,
        b.Kind.ToString(),
        b.Status.ToString(),
        b.SizeBytes,
        b.Error,
        b.StartedAt,
        b.CompletedAt,
        b.CreatedBy);
}

/// <summary>Nightly scheduled backup and retention purge.</summary>
public sealed class BackupMaintenance(
    IServiceScopeFactory scopes,
    IConfiguration configuration,
    TimeProvider time,
    ILogger<BackupMaintenance> logger) : BackgroundService
{
    public async Task<int> RunOnceAsync(CancellationToken ct)
    {
        await using var scope = scopes.CreateAsyncScope();
        var service = scope.ServiceProvider.GetRequiredService<BackupService>();
        await service.PurgeAsync(ct);

        if (!configuration.GetValue("Backups:ScheduledEnabled", false))
        {
            return 0;
        }

        var intervalHours = Math.Max(1, configuration.GetValue("Backups:IntervalHours", 24));
        var since = time.GetUtcNow().AddHours(-intervalHours);
        var db = scope.ServiceProvider.GetRequiredService<KekeBeautyDbContext>();
        var recentExists = await db.DatabaseBackups.AsNoTracking()
            .AnyAsync(b => b.Status == BackupStatus.Succeeded && b.StartedAt >= since, ct);
        if (recentExists)
        {
            return 0;
        }

        await service.CreateAsync(null, BackupKind.Scheduled, ct);
        return 1;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!configuration.GetValue("Backups:MaintenanceEnabled", true))
        {
            return;
        }

        using var timer = new PeriodicTimer(TimeSpan.FromHours(1), time);
        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            try
            {
                await RunOnceAsync(stoppingToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogError(ex, "Scheduled database backup check failed.");
            }
        }
    }
}
