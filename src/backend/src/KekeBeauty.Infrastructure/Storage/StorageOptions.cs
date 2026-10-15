namespace KekeBeauty.Infrastructure.Storage;

public sealed class StorageOptions
{
    public const string SectionName = "Storage";

    /// <summary><c>S3</c> (MinIO or any S3-compatible server) or <c>Memory</c> (tests, storage-less development).</summary>
    public string Provider { get; init; } = "S3";

    public string ServiceUrl { get; init; } = "http://localhost:9000";
    public string AccessKey { get; init; } = string.Empty;
    public string SecretKey { get; init; } = string.Empty;
    public string Region { get; init; } = "us-east-1";
}
