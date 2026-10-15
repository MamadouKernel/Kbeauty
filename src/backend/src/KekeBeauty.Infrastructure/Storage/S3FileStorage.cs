using System.Collections.Concurrent;
using System.Net;
using Amazon.Runtime;
using Amazon.S3;
using Amazon.S3.Model;
using Amazon.S3.Util;
using KekeBeauty.SharedKernel;
using Microsoft.Extensions.Options;

namespace KekeBeauty.Infrastructure.Storage;

/// <summary>MinIO / S3 implementation of <see cref="IFileStorage"/>. Buckets are created on first use and stay private.</summary>
public sealed class S3FileStorage : IFileStorage, IDisposable
{
    private readonly AmazonS3Client _client;
    private readonly ConcurrentDictionary<string, Task> _buckets = new();

    public S3FileStorage(IOptions<StorageOptions> options)
    {
        var settings = options.Value;
        _client = new AmazonS3Client(
            new BasicAWSCredentials(settings.AccessKey, settings.SecretKey),
            new AmazonS3Config
            {
                ServiceURL = settings.ServiceUrl,
                ForcePathStyle = true,
                AuthenticationRegion = settings.Region,
                // MinIO does not implement the newer default integrity checksums.
                RequestChecksumCalculation = RequestChecksumCalculation.WHEN_REQUIRED,
                ResponseChecksumValidation = ResponseChecksumValidation.WHEN_REQUIRED,
            });
    }

    public async Task PutAsync(string bucket, string key, Stream content, string contentType, CancellationToken cancellationToken = default)
    {
        await EnsureBucketAsync(bucket);
        await _client.PutObjectAsync(
            new PutObjectRequest { BucketName = bucket, Key = key, InputStream = content, ContentType = contentType, AutoCloseStream = false },
            cancellationToken);
    }

    public async Task<StoredFile?> OpenReadAsync(string bucket, string key, CancellationToken cancellationToken = default)
    {
        try
        {
            using var response = await _client.GetObjectAsync(bucket, key, cancellationToken);
            var buffer = new MemoryStream();
            await response.ResponseStream.CopyToAsync(buffer, cancellationToken);
            buffer.Position = 0;
            return new StoredFile(buffer, response.Headers.ContentType);
        }
        catch (AmazonS3Exception ex) when (ex.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }
    }

    public async Task<StoredObjectInfo?> StatAsync(string bucket, string key, CancellationToken cancellationToken = default)
    {
        try
        {
            var metadata = await _client.GetObjectMetadataAsync(bucket, key, cancellationToken);
            return new StoredObjectInfo(metadata.ContentLength, metadata.Headers.ContentType);
        }
        catch (AmazonS3Exception ex) when (ex.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }
    }

    public async Task<Stream?> OpenRangeAsync(string bucket, string key, long from, long to, CancellationToken cancellationToken = default)
    {
        try
        {
            var response = await _client.GetObjectAsync(new GetObjectRequest { BucketName = bucket, Key = key, ByteRange = new ByteRange(from, to) }, cancellationToken);
            return new ResponseStream(response);
        }
        catch (AmazonS3Exception ex) when (ex.StatusCode is HttpStatusCode.NotFound or HttpStatusCode.RequestedRangeNotSatisfiable)
        {
            return null;
        }
    }

    public async Task DeleteAsync(string bucket, string key, CancellationToken cancellationToken = default) =>
        await _client.DeleteObjectAsync(bucket, key, cancellationToken);

    public void Dispose() => _client.Dispose();

    /// <summary>Hands out the S3 response stream and releases the response with it.</summary>
    private sealed class ResponseStream(GetObjectResponse response) : Stream
    {
        private readonly Stream _inner = response.ResponseStream;

        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
        public override void Flush() => _inner.Flush();
        public override int Read(byte[] buffer, int offset, int count) => _inner.Read(buffer, offset, count);
        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default) => _inner.ReadAsync(buffer, cancellationToken);
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                response.Dispose();
            }

            base.Dispose(disposing);
        }
    }

    private Task EnsureBucketAsync(string bucket) => _buckets.GetOrAdd(bucket, async name =>
    {
        if (!await AmazonS3Util.DoesS3BucketExistV2Async(_client, name))
        {
            await _client.PutBucketAsync(name);
        }
    });
}
