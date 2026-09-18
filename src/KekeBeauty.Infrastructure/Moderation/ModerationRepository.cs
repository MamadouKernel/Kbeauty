using Dapper;
using KekeBeauty.Application.Moderation;

namespace KekeBeauty.Infrastructure.Moderation;

public sealed class ModerationRepository : IModerationRepository
{
    private readonly IDbConnectionFactory _connectionFactory;

    public ModerationRepository(IDbConnectionFactory connectionFactory)
    {
        _connectionFactory = connectionFactory;
    }

    public async Task<bool> SetUtilisateurSuspenduAsync(Guid idUtilisateur, bool suspendu, CancellationToken cancellationToken)
    {
        using var connection = _connectionFactory.CreateConnection();
        await connection.OpenAsync(cancellationToken);

        var rows = await connection.ExecuteAsync(new CommandDefinition(
            "UPDATE utilisateur SET est_suspendu = @suspendu WHERE id_utilisateur = @idUtilisateur;",
            new { idUtilisateur, suspendu },
            cancellationToken: cancellationToken));

        return rows > 0;
    }

    public async Task<bool> SetEtablissementSuspenduAsync(Guid idEtablissement, bool suspendu, CancellationToken cancellationToken)
    {
        using var connection = _connectionFactory.CreateConnection();
        await connection.OpenAsync(cancellationToken);

        var rows = await connection.ExecuteAsync(new CommandDefinition(
            "UPDATE etablissement SET est_suspendu = @suspendu WHERE id_etablissement = @idEtablissement;",
            new { idEtablissement, suspendu },
            cancellationToken: cancellationToken));

        return rows > 0;
    }
}
