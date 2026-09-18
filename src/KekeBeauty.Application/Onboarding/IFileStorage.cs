namespace KekeBeauty.Application.Onboarding;

public interface IFileStorage
{
    /// <summary>Ecrit le flux et retourne le chemin relatif stocke en base (FR-001).</summary>
    Task<string> SaveAsync(Guid idEtablissement, string fileType, string extension, Stream content, CancellationToken cancellationToken);

    /// <summary>Ouvre le fichier en lecture a partir de son chemin relatif (FR-006/FR-011).</summary>
    Task<Stream?> OpenAsync(string relativePath, CancellationToken cancellationToken);
}
