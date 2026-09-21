using Dapper;
using KekeBeauty.Application.Directory;

namespace KekeBeauty.Infrastructure.Listing;

public sealed class FavoriRepository : IFavoriRepository
{
    private readonly IDbConnectionFactory _connectionFactory;

    public FavoriRepository(IDbConnectionFactory connectionFactory)
    {
        _connectionFactory = connectionFactory;
    }

    public async Task<IReadOnlyList<EtablissementSummary>> ListerAsync(Guid idUtilisateurClient, CancellationToken cancellationToken)
    {
        using var connection = _connectionFactory.CreateConnection();
        await connection.OpenAsync(cancellationToken);
        var rows = await connection.QueryAsync<EtablissementSummary>(new CommandDefinition(
            @"SELECT e.id_etablissement AS IdEtablissement, e.nom_etablissement AS NomEtablissement,
                     c.libelle_commune AS LibelleCommune, e.gps_latitude AS GpsLatitude, e.gps_longitude AS GpsLongitude
              FROM favori f
              JOIN etablissement e ON e.id_etablissement = f.id_etablissement
              JOIN commune c ON c.id_commune = e.id_commune
              WHERE f.id_utilisateur_client = @idUtilisateurClient
                AND e.statut_kyc = 'VALIDE' AND e.est_suspendu = false
              ORDER BY e.nom_etablissement;",
            new { idUtilisateurClient }, cancellationToken: cancellationToken));
        return rows.AsList();
    }
    public async Task<bool> EstFavoriAsync(Guid idUtilisateurClient, Guid idEtablissement, CancellationToken cancellationToken)
    {
        using var connection = _connectionFactory.CreateConnection();
        await connection.OpenAsync(cancellationToken);

        var count = await connection.ExecuteScalarAsync<int>(new CommandDefinition(
            "SELECT COUNT(*) FROM favori WHERE id_utilisateur_client = @idUtilisateurClient AND id_etablissement = @idEtablissement;",
            new { idUtilisateurClient, idEtablissement },
            cancellationToken: cancellationToken));

        return count > 0;
    }

    public async Task<bool> ToggleAsync(Guid idUtilisateurClient, Guid idEtablissement, CancellationToken cancellationToken)
    {
        using var connection = _connectionFactory.CreateConnection();
        await connection.OpenAsync(cancellationToken);

        var dejaFavori = await EstFavoriAsync(idUtilisateurClient, idEtablissement, cancellationToken);

        if (dejaFavori)
        {
            await connection.ExecuteAsync(new CommandDefinition(
                "DELETE FROM favori WHERE id_utilisateur_client = @idUtilisateurClient AND id_etablissement = @idEtablissement;",
                new { idUtilisateurClient, idEtablissement },
                cancellationToken: cancellationToken));
            return false;
        }

        await connection.ExecuteAsync(new CommandDefinition(
            @"INSERT INTO favori (id_utilisateur_client, id_etablissement)
              VALUES (@idUtilisateurClient, @idEtablissement)
              ON CONFLICT DO NOTHING;",
            new { idUtilisateurClient, idEtablissement },
            cancellationToken: cancellationToken));
        return true;
    }
}

