using Dapper;
using KekeBeauty.Application.Directory;

namespace KekeBeauty.Infrastructure.Listing;

public sealed class PrestationRepository : IPrestationRepository
{
    private readonly IDbConnectionFactory _connectionFactory;

    public PrestationRepository(IDbConnectionFactory connectionFactory)
    {
        _connectionFactory = connectionFactory;
    }

    public async Task<Guid> AddAsync(Guid idEtablissement, string libellePrestation, decimal tarif, short dureeMinutes, CancellationToken cancellationToken)
    {
        using var connection = _connectionFactory.CreateConnection();
        await connection.OpenAsync(cancellationToken);

        return await connection.ExecuteScalarAsync<Guid>(new CommandDefinition(
            @"INSERT INTO prestation (libelle_prestation, tarif, duree_minutes, id_etablissement)
              VALUES (@libellePrestation, @tarif, @dureeMinutes, @idEtablissement)
              RETURNING id_prestation;",
            new { libellePrestation, tarif, dureeMinutes, idEtablissement },
            cancellationToken: cancellationToken));
    }
}
