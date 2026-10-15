using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using KekeBeauty.Infrastructure.Backups;
using KekeBeauty.Infrastructure.Persistence;
using KekeBeauty.Modules.Administration.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace KekeBeauty.IntegrationTests;

[Collection(ApiCollection.Name)]
public sealed class BackupTests(ApiFixture api)
{
    private const string Url = "/api/v1/admin/backups";

    private async Task WithDbAsync(Func<KekeBeautyDbContext, Task> action)
    {
        await using var scope = api.Services.CreateAsyncScope();
        await action(scope.ServiceProvider.GetRequiredService<KekeBeautyDbContext>());
    }

    [Fact]
    public async Task Super_admin_takes_lists_downloads_and_deletes_a_backup()
    {
        using var superAdmin = await TestAccounts.SuperAdminAsync(api);

        var created = await superAdmin.Client.PostAsync(Url, null);
        created.StatusCode.Should().Be(HttpStatusCode.Created);
        var backup = (await created.Content.ReadFromJsonAsync<BackupDto>())!;
        backup.Status.Should().Be("Succeeded");
        backup.Kind.Should().Be("Manual");
        backup.SizeBytes.Should().Be("PGDMP-fake-backup"u8.Length);
        backup.CreatedBy.Should().Be(superAdmin.UserId);

        (await superAdmin.Client.GetFromJsonAsync<List<BackupDto>>(Url))!.Should().Contain(b => b.Id == backup.Id);

        var download = await superAdmin.Client.GetAsync($"{Url}/{backup.Id}/download");
        download.StatusCode.Should().Be(HttpStatusCode.OK);
        download.Content.Headers.ContentDisposition!.FileName.Should().EndWith(".dump");
        (await download.Content.ReadAsByteArrayAsync()).Should().Equal("PGDMP-fake-backup"u8.ToArray());

        (await superAdmin.Client.DeleteAsync($"{Url}/{backup.Id}")).StatusCode.Should().Be(HttpStatusCode.NoContent);
        (await superAdmin.Client.GetAsync($"{Url}/{backup.Id}/download")).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await superAdmin.Client.DeleteAsync($"{Url}/{backup.Id}")).StatusCode.Should().Be(HttpStatusCode.NotFound);

        await WithDbAsync(async db =>
        {
            var actions = await db.AuditLogs.Where(a => a.EntityId == backup.Id).Select(a => a.Action).ToListAsync();
            actions.Should().BeEquivalentTo("backup.create", "backup.download", "backup.delete");
        });
    }

    [Fact]
    public async Task Only_the_super_admin_can_reach_the_backups()
    {
        using var admin = await TestAccounts.AdminAsync(api);
        using var anonymous = api.CreateClient();

        (await admin.Client.GetAsync(Url)).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await admin.Client.PostAsync(Url, null)).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await admin.Client.GetAsync($"{Url}/{Guid.NewGuid()}/download")).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await anonymous.GetAsync(Url)).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task A_failed_dump_is_recorded_and_cannot_be_downloaded()
    {
        using var superAdmin = await TestAccounts.SuperAdminAsync(api);
        var dumper = api.Services.GetRequiredService<FakeDatabaseDumper>();

        try
        {
            dumper.Succeed = false;
            var created = await superAdmin.Client.PostAsync(Url, null);
            created.StatusCode.Should().Be(HttpStatusCode.Created);
            var backup = (await created.Content.ReadFromJsonAsync<BackupDto>())!;
            backup.Status.Should().Be("Failed");
            backup.Error.Should().Contain("pg_dump");

            (await superAdmin.Client.GetAsync($"{Url}/{backup.Id}/download")).StatusCode.Should().Be(HttpStatusCode.Conflict);
            (await superAdmin.Client.DeleteAsync($"{Url}/{backup.Id}")).StatusCode.Should().Be(HttpStatusCode.NoContent);
        }
        finally
        {
            dumper.Succeed = true;
        }
    }

    [Fact]
    public async Task Retention_removes_old_backups_but_keeps_the_newest_ones()
    {
        await WithDbAsync(db => db.DatabaseBackups.ExecuteDeleteAsync());
        var now = api.Clock.GetUtcNow();
        await WithDbAsync(async db =>
        {
            for (var age = 1; age <= 6; age++)
            {
                db.DatabaseBackups.Add(new DatabaseBackup
                {
                    Kind = BackupKind.Scheduled,
                    Status = BackupStatus.Succeeded,
                    StartedAt = now.AddDays(-20 - age),
                });
            }

            db.DatabaseBackups.Add(new DatabaseBackup { Kind = BackupKind.Manual, Status = BackupStatus.Succeeded, StartedAt = now.AddDays(-2) });
            db.DatabaseBackups.Add(new DatabaseBackup { Kind = BackupKind.Manual, Status = BackupStatus.Failed, StartedAt = now.AddDays(-45) });
            await db.SaveChangesAsync();
        });

        await using var scope = api.Services.CreateAsyncScope();
        var removed = await scope.ServiceProvider.GetRequiredService<BackupService>().PurgeAsync(CancellationToken.None);

        // Kept: the recent one plus the two newest old ones (the newest three successes); removed: four old successes and the stale failure.
        removed.Should().Be(5);
        await WithDbAsync(async db => (await db.DatabaseBackups.CountAsync()).Should().Be(3));
        await WithDbAsync(db => db.DatabaseBackups.ExecuteDeleteAsync());
    }

    [Fact]
    public async Task The_scheduled_job_only_runs_when_enabled_and_no_backup_is_recent()
    {
        await WithDbAsync(db => db.DatabaseBackups.ExecuteDeleteAsync());
        var maintenance = api.Services.GetRequiredService<BackupMaintenance>();

        (await maintenance.RunOnceAsync(CancellationToken.None)).Should().Be(0);
        await WithDbAsync(async db => (await db.DatabaseBackups.CountAsync()).Should().Be(0));

        using var enabled = api.WithWebHostBuilder(builder => builder.UseSetting("Backups:ScheduledEnabled", "true"));
        var job = enabled.Services.GetRequiredService<BackupMaintenance>();
        (await job.RunOnceAsync(CancellationToken.None)).Should().Be(1);
        (await job.RunOnceAsync(CancellationToken.None)).Should().Be(0);
        await WithDbAsync(async db => (await db.DatabaseBackups.SingleAsync()).Kind.Should().Be(BackupKind.Scheduled));
        await WithDbAsync(db => db.DatabaseBackups.ExecuteDeleteAsync());
    }
}
