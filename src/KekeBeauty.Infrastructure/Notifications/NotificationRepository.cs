using Dapper;
using KekeBeauty.Application.Notifications;

namespace KekeBeauty.Infrastructure.Notifications;

public sealed class NotificationRepository : INotificationRepository
{
    private readonly IDbConnectionFactory _connectionFactory;

    public NotificationRepository(IDbConnectionFactory connectionFactory)
    {
        _connectionFactory = connectionFactory;
    }

    public async Task CreerAsync(Guid idUtilisateur, string titre, string message, Guid? idRdv, CancellationToken cancellationToken)
    {
        using var connection = _connectionFactory.CreateConnection();
        await connection.OpenAsync(cancellationToken);

        await connection.ExecuteAsync(new CommandDefinition(
            @"INSERT INTO notification (id_utilisateur, titre, message, id_rdv)
              VALUES (@idUtilisateur, @titre, @message, @idRdv);",
            new { idUtilisateur, titre, message, idRdv },
            cancellationToken: cancellationToken));
    }

    public async Task<IReadOnlyList<NotificationDto>> ListerAsync(Guid idUtilisateur, CancellationToken cancellationToken)
    {
        using var connection = _connectionFactory.CreateConnection();
        await connection.OpenAsync(cancellationToken);

        var rows = await connection.QueryAsync<NotificationDto>(new CommandDefinition(
            @"SELECT id_notification AS IdNotification, titre AS Titre, message AS Message,
                     id_rdv AS IdRdv, lu AS Lu, date_creation AS DateCreation
              FROM notification
              WHERE id_utilisateur = @idUtilisateur
              ORDER BY date_creation DESC
              LIMIT 50;",
            new { idUtilisateur },
            cancellationToken: cancellationToken));

        return rows.AsList();
    }

    public async Task<int> CompterNonLuesAsync(Guid idUtilisateur, CancellationToken cancellationToken)
    {
        using var connection = _connectionFactory.CreateConnection();
        await connection.OpenAsync(cancellationToken);

        return await connection.ExecuteScalarAsync<int>(new CommandDefinition(
            "SELECT COUNT(*) FROM notification WHERE id_utilisateur = @idUtilisateur AND lu = false;",
            new { idUtilisateur },
            cancellationToken: cancellationToken));
    }

    public async Task MarquerToutesLuesAsync(Guid idUtilisateur, CancellationToken cancellationToken)
    {
        using var connection = _connectionFactory.CreateConnection();
        await connection.OpenAsync(cancellationToken);

        await connection.ExecuteAsync(new CommandDefinition(
            "UPDATE notification SET lu = true WHERE id_utilisateur = @idUtilisateur AND lu = false;",
            new { idUtilisateur },
            cancellationToken: cancellationToken));
    }
}
