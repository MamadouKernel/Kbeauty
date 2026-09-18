using KekeBeauty.Application.Onboarding;
using Microsoft.Extensions.Configuration;

namespace KekeBeauty.Infrastructure.Onboarding;

public sealed class LocalFileStorage : IFileStorage
{
    private readonly string _rootPath;

    public LocalFileStorage(IConfiguration configuration)
    {
        _rootPath = configuration["Storage:KycRootPath"] ?? "/app/storage/kyc";
    }

    public async Task<string> SaveAsync(Guid idEtablissement, string fileType, string extension, Stream content, CancellationToken cancellationToken)
    {
        var safeExtension = extension.TrimStart('.').ToLowerInvariant();
        var relativePath = $"{idEtablissement}/{fileType}.{safeExtension}";
        var fullPath = Path.Combine(_rootPath, idEtablissement.ToString(), $"{fileType}.{safeExtension}");

        Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);

        using var fileStream = File.Create(fullPath);
        await content.CopyToAsync(fileStream, cancellationToken);

        return relativePath;
    }

    public Task<Stream?> OpenAsync(string relativePath, CancellationToken cancellationToken)
    {
        var fullPath = Path.Combine(_rootPath, relativePath);
        if (!File.Exists(fullPath))
        {
            return Task.FromResult<Stream?>(null);
        }

        Stream stream = File.OpenRead(fullPath);
        return Task.FromResult<Stream?>(stream);
    }
}
