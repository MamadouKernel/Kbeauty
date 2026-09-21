using Dapper;
using KekeBeauty.Application.Partner;

namespace KekeBeauty.Infrastructure.Partner;

public sealed class PartnerStatsRepository : IPartnerStatsRepository
{
    private readonly IDbConnectionFactory _connectionFactory;

    public PartnerStatsRepository(IDbConnectionFactory connectionFactory)
    {
        _connectionFactory = connectionFactory;
    }

    public async Task<PartnerStatistiques> GetStatistiquesAsync(Guid idEtablissement, CancellationToken cancellationToken)
    {
        using var connection = _connectionFactory.CreateConnection();
        await connection.OpenAsync(cancellationToken);

        var stats = await connection.QuerySingleAsync<PartnerStatistiques>(new CommandDefinition(
            @"SELECT
                COUNT(*) FILTER (WHERE r.statut_rdv = 'CONFIRME') AS NombreRdvConfirmes,
                COUNT(*) FILTER (WHERE r.statut_rdv = 'TERMINE') AS NombreRdvTermines,
                COALESCE(SUM(p.tarif) FILTER (WHERE r.statut_rdv IN ('CONFIRME', 'TERMINE')), 0) AS RevenuEstime
              FROM rdv r
              JOIN prestation p ON p.id_prestation = r.id_prestation
              WHERE r.id_etablissement = @idEtablissement;",
            new { idEtablissement },
            cancellationToken: cancellationToken));

        stats.StatutAbonnement = await connection.ExecuteScalarAsync<string?>(new CommandDefinition(
            @"SELECT statut_abonnement FROM abonnement
              WHERE id_etablissement = @idEtablissement
              ORDER BY date_debut_engagement DESC LIMIT 1;",
            new { idEtablissement },
            cancellationToken: cancellationToken));

        return stats;
    }
}
