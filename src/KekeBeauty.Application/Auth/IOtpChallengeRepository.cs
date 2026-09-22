namespace KekeBeauty.Application.Auth;

public static class OtpChallengePolicy
{
    /// <summary>Audit securite : au-dela de ce nombre d'echecs, le challenge est invalide
    /// independamment du rate limiting par IP (qu'un attaquant distribue sur plusieurs IP peut
    /// contourner pour un meme numero de telephone cible).</summary>
    public const int MaxAttempts = 5;
}

public sealed class OtpChallenge
{
    public Guid Id { get; set; }
    public string Telephone { get; set; } = string.Empty;
    public string CodeHash { get; set; } = string.Empty;
    public DateTimeOffset ExpiresAt { get; set; }
    public string Status { get; set; } = string.Empty;
    public int Attempts { get; set; }
}

public interface IOtpChallengeRepository
{
    /// <summary>
    /// Invalide tout challenge PENDING existant pour (telephone, typeCompte) puis en cree un
    /// nouveau, dans la meme transaction (FR-006).
    /// </summary>
    Task<Guid> CreateAndInvalidatePreviousAsync(
        string telephone, string typeCompte, string codeHash, DateTimeOffset expiresAt, CancellationToken cancellationToken);

    Task<OtpChallenge?> GetActivePendingAsync(string telephone, string typeCompte, CancellationToken cancellationToken);

    Task MarkConsumedAsync(Guid id, CancellationToken cancellationToken);

    /// <summary>
    /// Incremente le compteur d'echecs du challenge de facon atomique et l'invalide (status
    /// INVALIDATED) si le seuil OtpChallengePolicy.MaxAttempts est atteint. Retourne le nombre
    /// d'essais apres incrementation.
    /// </summary>
    Task<int> RegisterFailedAttemptAsync(Guid id, CancellationToken cancellationToken);
}
