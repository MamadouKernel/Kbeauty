using Dapper;
using KekeBeauty.Application.Rdv;

namespace KekeBeauty.Infrastructure.Rdv;

public sealed class LitigeRepository : ILitigeRepository
{
    private readonly IDbConnectionFactory _connectionFactory;

    public LitigeRepository(IDbConnectionFactory connectionFactory)
    {
        _connectionFactory = connectionFactory;
    }

    public async Task<bool> EstPartiePrenanteAsync(Guid idRdv, Guid idUtilisateur, CancellationToken cancellationToken)
    {
        using var connection = _connectionFactory.CreateConnection();
        await connection.OpenAsync(cancellationToken);

        var count = await connection.ExecuteScalarAsync<int>(new CommandDefinition(
            @"SELECT COUNT(*) FROM rdv r
              JOIN etablissement e ON e.id_etablissement = r.id_etablissement
              WHERE r.id_rdv = @idRdv AND (r.id_utilisateur_client = @idUtilisateur OR e.id_utilisateur_gerant = @idUtilisateur);",
            new { idRdv, idUtilisateur },
            cancellationToken: cancellationToken));

        return count > 0;
    }

    public async Task<Guid> DeclarerAsync(Guid idRdv, Guid idUtilisateurDeclarant, string motif, CancellationToken cancellationToken)
    {
        using var connection = _connectionFactory.CreateConnection();
        await connection.OpenAsync(cancellationToken);

        return await connection.ExecuteScalarAsync<Guid>(new CommandDefinition(
            @"INSERT INTO litige (id_rdv, id_utilisateur_declarant, motif)
              VALUES (@idRdv, @idUtilisateurDeclarant, @motif)
              RETURNING id_litige;",
            new { idRdv, idUtilisateurDeclarant, motif },
            cancellationToken: cancellationToken));
    }

    public async Task<IReadOnlyList<LitigeDto>> ListerAsync(string? statut, CancellationToken cancellationToken)
    {
        using var connection = _connectionFactory.CreateConnection();
        await connection.OpenAsync(cancellationToken);

        var rows = await connection.QueryAsync<LitigeDto>(new CommandDefinition(
            @"SELECT l.id_litige AS IdLitige, l.id_rdv AS IdRdv, l.motif AS Motif,
                     l.statut_litige AS StatutLitige, l.resolution AS Resolution, l.date_creation AS DateCreation,
                     e.nom_etablissement AS NomEtablissement, u.telephone AS TelephoneDeclarant
              FROM litige l
              JOIN rdv r ON r.id_rdv = l.id_rdv
              JOIN etablissement e ON e.id_etablissement = r.id_etablissement
              JOIN utilisateur u ON u.id_utilisateur = l.id_utilisateur_declarant
              WHERE @statut IS NULL OR l.statut_litige = @statut
              ORDER BY l.date_creation DESC;",
            new { statut },
            cancellationToken: cancellationToken));

        return rows.AsList();
    }

    public async Task<bool> ResoudreAsync(Guid idLitige, string resolution, CancellationToken cancellationToken)
    {
        using var connection = _connectionFactory.CreateConnection();
        await connection.OpenAsync(cancellationToken);

        var rows = await connection.ExecuteAsync(new CommandDefinition(
            @"UPDATE litige SET statut_litige = 'RESOLU', resolution = @resolution, date_resolution = now()
              WHERE id_litige = @idLitige AND statut_litige = 'OUVERT';",
            new { idLitige, resolution },
            cancellationToken: cancellationToken));

        return rows > 0;
    }
}
