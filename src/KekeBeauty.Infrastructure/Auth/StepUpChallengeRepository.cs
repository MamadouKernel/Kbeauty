using Dapper;
using KekeBeauty.Application.Auth;

namespace KekeBeauty.Infrastructure.Auth;

public sealed class StepUpChallengeRepository : IStepUpChallengeRepository
{
    private readonly IDbConnectionFactory _connectionFactory;

    public StepUpChallengeRepository(IDbConnectionFactory connectionFactory)
    {
        _connectionFactory = connectionFactory;
    }

    public async Task CreateAndInvalidatePreviousAsync(Guid idUtilisateur, string codeHash, DateTimeOffset expiresAt, CancellationToken cancellationToken)
    {
        using var connection = _connectionFactory.CreateConnection();
        await connection.OpenAsync(cancellationToken);
        using var transaction = connection.BeginTransaction();

        await connection.ExecuteAsync(new CommandDefinition(
            @"UPDATE step_up_challenge SET status = 'EXPIRED'
              WHERE id_utilisateur = @idUtilisateur AND status = 'PENDING';",
            new { idUtilisateur },
            transaction: transaction,
            cancellationToken: cancellationToken));

        await connection.ExecuteAsync(new CommandDefinition(
            @"INSERT INTO step_up_challenge (id_utilisateur, code_hash, expires_at, status)
              VALUES (@idUtilisateur, @codeHash, @expiresAt, 'PENDING');",
            new { idUtilisateur, codeHash, expiresAt },
            transaction: transaction,
            cancellationToken: cancellationToken));

        transaction.Commit();
    }

    public async Task<bool> TryConsumeAsync(Guid idUtilisateur, string codeHash, CancellationToken cancellationToken)
    {
        using var connection = _connectionFactory.CreateConnection();
        await connection.OpenAsync(cancellationToken);

        var rows = await connection.ExecuteAsync(new CommandDefinition(
            @"UPDATE step_up_challenge SET status = 'CONSUMED'
              WHERE id_utilisateur = @idUtilisateur AND code_hash = @codeHash AND status = 'PENDING' AND expires_at > now();",
            new { idUtilisateur, codeHash },
            cancellationToken: cancellationToken));

        return rows > 0;
    }
}
