using KekeBeauty.SharedKernel;

namespace KekeBeauty.Modules.Administration.Domain;

public enum BackupStatus
{
    Running,
    Succeeded,
    Failed,
}

public enum BackupKind
{
    Manual,
    Scheduled,
}

/// <summary>A PostgreSQL dump taken from the back-office (or by the nightly job) and kept in the private backups bucket.</summary>
public sealed class DatabaseBackup : Entity
{
    public required BackupKind Kind { get; init; }
    public BackupStatus Status { get; set; } = BackupStatus.Running;
    public string? FileKey { get; set; }
    public long SizeBytes { get; set; }
    public string? Error { get; set; }
    public required DateTimeOffset StartedAt { get; init; }
    public DateTimeOffset? CompletedAt { get; set; }

    /// <summary>The SuperAdmin who asked for it; null for scheduled backups.</summary>
    public Guid? CreatedBy { get; init; }
}
