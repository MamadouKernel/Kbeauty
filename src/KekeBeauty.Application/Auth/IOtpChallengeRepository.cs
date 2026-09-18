namespace KekeBeauty.Application.Auth;

public sealed class OtpChallenge
{
    public Guid Id { get; set; }
    public string Telephone { get; set; } = string.Empty;
    public string CodeHash { get; set; } = string.Empty;
    public DateTimeOffset ExpiresAt { get; set; }
    public string Status { get; set; } = string.Empty;
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
}
