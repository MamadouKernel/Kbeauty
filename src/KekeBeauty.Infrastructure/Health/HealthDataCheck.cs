using Dapper;
using KekeBeauty.Application.Health;

namespace KekeBeauty.Infrastructure.Health;

public sealed class HealthDataCheck : IHealthDataCheck
{
    private readonly IDbConnectionFactory _connectionFactory;

    public HealthDataCheck(IDbConnectionFactory connectionFactory)
    {
        _connectionFactory = connectionFactory;
    }

    public async Task<HealthDataCheckResult> CheckAsync(CancellationToken cancellationToken)
    {
        try
        {
            using var connection = _connectionFactory.CreateConnection();
            await connection.OpenAsync(cancellationToken);

            // Lecture reelle.
            await connection.ExecuteScalarAsync<int>(new CommandDefinition(
                "SELECT 1;", cancellationToken: cancellationToken));

            // Ecriture reelle, jamais persistee : INSERT puis ROLLBACK dans la meme transaction
            // (voir specs/002-scaffold-dotnet/research.md, Decision 3).
            using var transaction = connection.BeginTransaction();
            await connection.ExecuteAsync(new CommandDefinition(
                "INSERT INTO health_check (id, checked_at) VALUES (gen_random_uuid(), now());",
                transaction: transaction,
                cancellationToken: cancellationToken));
            transaction.Rollback();

            return new HealthDataCheckResult(CanRead: true, CanWrite: true);
        }
        catch (Exception ex)
        {
            return new HealthDataCheckResult(CanRead: false, CanWrite: false, ErrorMessage: ex.Message);
        }
    }
}
