using Dapper;
using KekeBeauty.Application.Notifications;

namespace KekeBeauty.Infrastructure.Notifications;

public sealed class PushSubscriptionRepository : IPushSubscriptionRepository
{
    private readonly IDbConnectionFactory _connectionFactory;

    public PushSubscriptionRepository(IDbConnectionFactory connectionFactory)
    {
        _connectionFactory = connectionFactory;
    }

    public async Task EnregistrerAsync(Guid idUtilisateur, string endpoint, string p256dh, string auth, CancellationToken cancellationToken)
    {
        using var connection = _connectionFactory.CreateConnection();
        await connection.OpenAsync(cancellationToken);

        await connection.ExecuteAsync(new CommandDefinition(
            @"INSERT INTO push_subscription (id_utilisateur, endpoint, p256dh, auth)
              VALUES (@idUtilisateur, @endpoint, @p256dh, @auth)
              ON CONFLICT (endpoint) DO UPDATE SET id_utilisateur = @idUtilisateur, p256dh = @p256dh, auth = @auth;",
            new { idUtilisateur, endpoint, p256dh, auth },
            cancellationToken: cancellationToken));
    }

    public async Task<IReadOnlyList<PushSubscriptionDto>> ListerParUtilisateurAsync(Guid idUtilisateur, CancellationToken cancellationToken)
    {
        using var connection = _connectionFactory.CreateConnection();
        await connection.OpenAsync(cancellationToken);

        var rows = await connection.QueryAsync<PushSubscriptionDto>(new CommandDefinition(
            "SELECT endpoint AS Endpoint, p256dh AS P256dh, auth AS Auth FROM push_subscription WHERE id_utilisateur = @idUtilisateur;",
            new { idUtilisateur },
            cancellationToken: cancellationToken));

        return rows.AsList();
    }

    public async Task SupprimerAsync(string endpoint, CancellationToken cancellationToken)
    {
        using var connection = _connectionFactory.CreateConnection();
        await connection.OpenAsync(cancellationToken);

        await connection.ExecuteAsync(new CommandDefinition(
            "DELETE FROM push_subscription WHERE endpoint = @endpoint;",
            new { endpoint },
            cancellationToken: cancellationToken));
    }
}
