using Dapper;
using KekeBeauty.Application.Partner;

namespace KekeBeauty.Infrastructure.Partner;

public sealed class CollaborateurRepository : ICollaborateurRepository
{
    private readonly IDbConnectionFactory _connectionFactory;

    public CollaborateurRepository(IDbConnectionFactory connectionFactory)
    {
        _connectionFactory = connectionFactory;
    }

    public async Task<Guid> AjouterAsync(Guid idEtablissement, string nom, string? specialite, string? telephone, CancellationToken cancellationToken)
    {
        using var connection = _connectionFactory.CreateConnection();
        await connection.OpenAsync(cancellationToken);

        return await connection.ExecuteScalarAsync<Guid>(new CommandDefinition(
            @"INSERT INTO collaborateur (nom, specialite, telephone, id_etablissement)
              VALUES (@nom, @specialite, @telephone, @idEtablissement)
              RETURNING id_collaborateur;",
            new { nom, specialite, telephone, idEtablissement },
            cancellationToken: cancellationToken));
    }

    public async Task<IReadOnlyList<CollaborateurDto>> ListerAsync(Guid idEtablissement, CancellationToken cancellationToken)
    {
        using var connection = _connectionFactory.CreateConnection();
        await connection.OpenAsync(cancellationToken);

        var rows = await connection.QueryAsync<CollaborateurDto>(new CommandDefinition(
            @"SELECT id_collaborateur AS IdCollaborateur, nom AS Nom, specialite AS Specialite,
                     telephone AS Telephone, (id_utilisateur IS NOT NULL) AS CompteActif
              FROM collaborateur WHERE id_etablissement = @idEtablissement ORDER BY date_creation;",
            new { idEtablissement },
            cancellationToken: cancellationToken));

        return rows.AsList();
    }

    public async Task<bool> RetirerAsync(Guid idEtablissement, Guid idCollaborateur, CancellationToken cancellationToken)
    {
        using var connection = _connectionFactory.CreateConnection();
        await connection.OpenAsync(cancellationToken);

        var rows = await connection.ExecuteAsync(new CommandDefinition(
            "DELETE FROM collaborateur WHERE id_collaborateur = @idCollaborateur AND id_etablissement = @idEtablissement;",
            new { idCollaborateur, idEtablissement },
            cancellationToken: cancellationToken));

        return rows > 0;
    }

    public async Task<Guid?> LinkUtilisateurByTelephoneAsync(string telephone, Guid idUtilisateur, CancellationToken cancellationToken)
    {
        using var connection = _connectionFactory.CreateConnection();
        await connection.OpenAsync(cancellationToken);

        return await connection.ExecuteScalarAsync<Guid?>(new CommandDefinition(
            @"UPDATE collaborateur SET id_utilisateur = @idUtilisateur
              WHERE telephone = @telephone AND id_utilisateur IS NULL
              RETURNING id_collaborateur;",
            new { telephone, idUtilisateur },
            cancellationToken: cancellationToken));
    }

    public async Task<Guid?> GetIdCollaborateurByUtilisateurAsync(Guid idUtilisateur, CancellationToken cancellationToken)
    {
        using var connection = _connectionFactory.CreateConnection();
        await connection.OpenAsync(cancellationToken);

        return await connection.ExecuteScalarAsync<Guid?>(new CommandDefinition(
            "SELECT id_collaborateur FROM collaborateur WHERE id_utilisateur = @idUtilisateur;",
            new { idUtilisateur },
            cancellationToken: cancellationToken));
    }

    public async Task<bool> AppartientAEtablissementAsync(Guid idCollaborateur, Guid idEtablissement, CancellationToken cancellationToken)
    {
        using var connection = _connectionFactory.CreateConnection();
        await connection.OpenAsync(cancellationToken);

        var count = await connection.ExecuteScalarAsync<int>(new CommandDefinition(
            "SELECT COUNT(*) FROM collaborateur WHERE id_collaborateur = @idCollaborateur AND id_etablissement = @idEtablissement;",
            new { idCollaborateur, idEtablissement },
            cancellationToken: cancellationToken));

        return count > 0;
    }
}
