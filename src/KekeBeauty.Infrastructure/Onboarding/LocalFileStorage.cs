using KekeBeauty.Application.Onboarding;
using Microsoft.Extensions.Configuration;
using System.Text.RegularExpressions;

namespace KekeBeauty.Infrastructure.Onboarding;

public sealed class LocalFileStorage : IFileStorage
{
    private static readonly Regex SafeSegment = new("^[a-z0-9-]+$", RegexOptions.Compiled | RegexOptions.CultureInvariant);
    private static readonly HashSet<string> AllowedExtensions = new(StringComparer.OrdinalIgnoreCase) { "jpg", "jpeg", "png", "webp", "pdf" };
    private readonly string _rootPath;
    private readonly string _rootPrefix;

    public LocalFileStorage(IConfiguration configuration)
    {
        _rootPath = Path.GetFullPath(configuration["Storage:KycRootPath"] ?? "/app/storage/kyc");
        _rootPrefix = _rootPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
    }

    public async Task<string> SaveAsync(Guid idEtablissement, string fileType, string extension, Stream content, CancellationToken cancellationToken)
    {
        var safeExtension = extension.TrimStart('.').ToLowerInvariant();
        var safeFileType = fileType.Trim().ToLowerInvariant();
        if (!AllowedExtensions.Contains(safeExtension) || !SafeSegment.IsMatch(safeFileType))
            throw new InvalidOperationException("Nom ou extension de fichier non autorisé.");
        var relativePath = Path.Combine(idEtablissement.ToString("D"), $"{safeFileType}.{safeExtension}").Replace('\\', '/');
        var fullPath = ResolveInsideRoot(relativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
        await using var fileStream = new FileStream(fullPath, FileMode.Create, FileAccess.Write, FileShare.None, 81920, FileOptions.Asynchronous);
        await content.CopyToAsync(fileStream, cancellationToken);
        await fileStream.FlushAsync(cancellationToken);
        return relativePath;
    }

    public Task<Stream?> OpenAsync(string relativePath, CancellationToken cancellationToken)
    {
        string fullPath;
        try { fullPath = ResolveInsideRoot(relativePath); }
        catch (InvalidOperationException) { return Task.FromResult<Stream?>(null); }
        if (!File.Exists(fullPath)) return Task.FromResult<Stream?>(null);
        Stream stream = new FileStream(fullPath, FileMode.Open, FileAccess.Read, FileShare.Read, 81920, FileOptions.Asynchronous);
        return Task.FromResult<Stream?>(stream);
    }

    public Task DeleteApplicationFilesAsync(Guid idEtablissement, CancellationToken cancellationToken)
    {
        var directory = ResolveInsideRoot(idEtablissement.ToString("D"));
        if (Directory.Exists(directory)) Directory.Delete(directory, recursive: true);
        return Task.CompletedTask;
    }

    private string ResolveInsideRoot(string relativePath)
    {
        if (string.IsNullOrWhiteSpace(relativePath) || Path.IsPathRooted(relativePath))
            throw new InvalidOperationException("Chemin de stockage invalide.");
        var fullPath = Path.GetFullPath(Path.Combine(_rootPath, relativePath));
        if (!fullPath.StartsWith(_rootPrefix, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Chemin hors du stockage KYC.");
        return fullPath;
    }
}