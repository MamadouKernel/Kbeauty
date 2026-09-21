namespace KekeBeauty.Application.Auth;

public interface IStepUpChallengeRepository
{
    Task CreateAndInvalidatePreviousAsync(Guid idUtilisateur, string codeHash, DateTimeOffset expiresAt, CancellationToken cancellationToken);

    Task<bool> TryConsumeAsync(Guid idUtilisateur, string codeHash, CancellationToken cancellationToken);
}
