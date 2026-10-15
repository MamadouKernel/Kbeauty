namespace KekeBeauty.SharedKernel;

public static class StorageBuckets
{
    /// <summary>Establishment photos, served publicly through the API.</summary>
    public const string PublicMedia = "public-media";

    /// <summary>Identity documents: never exposed publicly, read only by admins through the API.</summary>
    public const string PrivateKyc = "private-kyc";

    /// <summary>Database dumps taken from the back-office: never public, downloaded by the SuperAdmin only.</summary>
    public const string PrivateBackups = "private-backups";
}

/// <summary>A stored object opened for reading. The caller owns (and must dispose) <see cref="Content"/>.</summary>
public sealed record StoredFile(Stream Content, string ContentType);

/// <summary>Size and type of a stored object.</summary>
public sealed record StoredObjectInfo(long Length, string ContentType);

/// <summary>Object storage port (PRD §10.4): MinIO/S3 in production, an in-memory fake in tests.</summary>
public interface IFileStorage
{
    Task PutAsync(string bucket, string key, Stream content, string contentType, CancellationToken cancellationToken = default);

    Task<StoredFile?> OpenReadAsync(string bucket, string key, CancellationToken cancellationToken = default);

    Task DeleteAsync(string bucket, string key, CancellationToken cancellationToken = default);

    /// <summary>Size and type of an object without reading it; null when it does not exist.</summary>
    Task<StoredObjectInfo?> StatAsync(string bucket, string key, CancellationToken cancellationToken = default);

    /// <summary>Bytes <paramref name="from"/> to <paramref name="to"/> (both inclusive) of an object, for streaming media with HTTP range requests.</summary>
    Task<Stream?> OpenRangeAsync(string bucket, string key, long from, long to, CancellationToken cancellationToken = default);
}

/// <summary>Produces a full dump of the application database into a file (<c>pg_dump</c> in production, a stub in tests).</summary>
public interface IDatabaseDumper
{
    Task<Result> DumpAsync(string destinationPath, CancellationToken cancellationToken = default);
}
