using System.Collections.Concurrent;
using KekeBeauty.SharedKernel;

namespace KekeBeauty.Infrastructure.Storage;

/// <summary>Volatile <see cref="IFileStorage"/> for tests and for running the API without MinIO.</summary>
public sealed class InMemoryFileStorage : IFileStorage
{
    private readonly ConcurrentDictionary<(string Bucket, string Key), (byte[] Bytes, string ContentType)> _objects = new();

    public IReadOnlyCollection<(string Bucket, string Key)> Keys => _objects.Keys.ToArray();

    public async Task PutAsync(string bucket, string key, Stream content, string contentType, CancellationToken cancellationToken = default)
    {
        // Like S3, which must know the object's length before it is sent: a stream that cannot report it is refused here too.
        if (!content.CanSeek)
        {
            throw new NotSupportedException("The storage needs a seekable stream (its length must be known up front).");
        }

        using var copy = new MemoryStream();
        await content.CopyToAsync(copy, cancellationToken);
        _objects[(bucket, key)] = (copy.ToArray(), contentType);
    }

    public Task<StoredFile?> OpenReadAsync(string bucket, string key, CancellationToken cancellationToken = default) =>
        Task.FromResult(_objects.TryGetValue((bucket, key), out var item)
            ? new StoredFile(new MemoryStream(item.Bytes), item.ContentType)
            : null);

    public Task<StoredObjectInfo?> StatAsync(string bucket, string key, CancellationToken cancellationToken = default) =>
        Task.FromResult(_objects.TryGetValue((bucket, key), out var item) ? new StoredObjectInfo(item.Bytes.Length, item.ContentType) : null);

    public Task<Stream?> OpenRangeAsync(string bucket, string key, long from, long to, CancellationToken cancellationToken = default) =>
        Task.FromResult<Stream?>(_objects.TryGetValue((bucket, key), out var item) && from >= 0 && to >= from && from < item.Bytes.Length
            ? new MemoryStream(item.Bytes, (int)from, (int)(Math.Min(to, item.Bytes.Length - 1) - from + 1), writable: false)
            : null);

    public Task DeleteAsync(string bucket, string key, CancellationToken cancellationToken = default)
    {
        _objects.TryRemove((bucket, key), out _);
        return Task.CompletedTask;
    }
}
