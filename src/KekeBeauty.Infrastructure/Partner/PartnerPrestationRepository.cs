using Dapper;
using KekeBeauty.Application.Partner;

namespace KekeBeauty.Infrastructure.Partner;

public sealed class PartnerPrestationRepository : IPartnerPrestationRepository
{
    private readonly IDbConnectionFactory _connectionFactory;

    public PartnerPrestationRepository(IDbConnectionFactory connectionFactory)
    {
        _connectionFactory = connectionFactory;
    }

    public async Task<Guid?> GetOwnerIdAsync(Guid idEtablissement, CancellationToken cancellationToken)
    {
        using var connection = _connectionFactory.CreateConnection();
        await connection.OpenAsync(cancellationToken);

        return await connection.ExecuteScalarAsync<Guid?>(new CommandDefinition(
            "SELECT id_utilisateur_gerant FROM etablissement WHERE id_etablissement = @idEtablissement;",
            new { idEtablissement },
            cancellationToken: cancellationToken));
    }

    public async Task<Guid> AddAsync(Guid idEtablissement, string libelle, decimal tarif, short dureeMinutes, CancellationToken cancellationToken)
    {
        using var connection = _connectionFactory.CreateConnection();
        await connection.OpenAsync(cancellationToken);

        return await connection.ExecuteScalarAsync<Guid>(new CommandDefinition(
            @"INSERT INTO prestation (libelle_prestation, tarif, duree_minutes, id_etablissement)
              VALUES (@libelle, @tarif, @dureeMinutes, @idEtablissement) RETURNING id_prestation;",
            new { libelle, tarif, dureeMinutes, idEtablissement },
            cancellationToken: cancellationToken));
    }

    public async Task<bool> UpdateAsync(Guid idEtablissement, Guid idPrestation, string libelle, decimal tarif, short dureeMinutes, CancellationToken cancellationToken)
    {
        using var connection = _connectionFactory.CreateConnection();
        await connection.OpenAsync(cancellationToken);

        var rows = await connection.ExecuteAsync(new CommandDefinition(
            @"UPDATE prestation SET libelle_prestation = @libelle, tarif = @tarif, duree_minutes = @dureeMinutes
              WHERE id_prestation = @idPrestation AND id_etablissement = @idEtablissement;",
            new { libelle, tarif, dureeMinutes, idPrestation, idEtablissement },
            cancellationToken: cancellationToken));

        return rows > 0;
    }

    public async Task<bool> DeleteAsync(Guid idEtablissement, Guid idPrestation, CancellationToken cancellationToken)
    {
        using var connection = _connectionFactory.CreateConnection();
        await connection.OpenAsync(cancellationToken);

        var rows = await connection.ExecuteAsync(new CommandDefinition(
            "DELETE FROM prestation WHERE id_prestation = @idPrestation AND id_etablissement = @idEtablissement;",
            new { idPrestation, idEtablissement },
            cancellationToken: cancellationToken));

        return rows > 0;
    }
}
