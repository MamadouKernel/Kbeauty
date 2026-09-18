using Dapper;
using KekeBeauty.Application.Auth;

namespace KekeBeauty.Infrastructure.Auth;

public sealed class OtpChallengeRepository : IOtpChallengeRepository
{
    private readonly IDbConnectionFactory _connectionFactory;

    public OtpChallengeRepository(IDbConnectionFactory connectionFactory)
    {
        _connectionFactory = connectionFactory;
    }

    public async Task<Guid> CreateAndInvalidatePreviousAsync(
        string telephone, string typeCompte, string codeHash, DateTimeOffset expiresAt, CancellationToken cancellationToken)
    {
        using var connection = _connectionFactory.CreateConnection();
        await connection.OpenAsync(cancellationToken);
        using var transaction = connection.BeginTransaction();

        await connection.ExecuteAsync(new CommandDefinition(
            @"UPDATE otp_challenge
              SET status = 'INVALIDATED'
              WHERE telephone = @telephone AND type_compte = @typeCompte::type_compte_enum AND status = 'PENDING';",
            new { telephone, typeCompte },
            transaction: transaction,
            cancellationToken: cancellationToken));

        var id = await connection.ExecuteScalarAsync<Guid>(new CommandDefinition(
            @"INSERT INTO otp_challenge (telephone, type_compte, code_hash, expires_at, status)
              VALUES (@telephone, @typeCompte::type_compte_enum, @codeHash, @expiresAt, 'PENDING')
              RETURNING id;",
            new { telephone, typeCompte, codeHash, expiresAt },
            transaction: transaction,
            cancellationToken: cancellationToken));

        transaction.Commit();
        return id;
    }

    public async Task<OtpChallenge?> GetActivePendingAsync(string telephone, string typeCompte, CancellationToken cancellationToken)
    {
        using var connection = _connectionFactory.CreateConnection();
        await connection.OpenAsync(cancellationToken);

        return await connection.QuerySingleOrDefaultAsync<OtpChallenge>(new CommandDefinition(
            @"SELECT id AS Id, telephone AS Telephone, code_hash AS CodeHash, expires_at AS ExpiresAt, status AS Status
              FROM otp_challenge
              WHERE telephone = @telephone AND type_compte = @typeCompte::type_compte_enum AND status = 'PENDING'
              ORDER BY created_at DESC
              LIMIT 1;",
            new { telephone, typeCompte },
            cancellationToken: cancellationToken));
    }

    public async Task MarkConsumedAsync(Guid id, CancellationToken cancellationToken)
    {
        using var connection = _connectionFactory.CreateConnection();
        await connection.OpenAsync(cancellationToken);

        await connection.ExecuteAsync(new CommandDefinition(
            "UPDATE otp_challenge SET status = 'CONSUMED' WHERE id = @id;",
            new { id },
            cancellationToken: cancellationToken));
    }
}
