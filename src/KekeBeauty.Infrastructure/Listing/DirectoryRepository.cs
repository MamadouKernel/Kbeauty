using Dapper;
using KekeBeauty.Application.Directory;

namespace KekeBeauty.Infrastructure.Listing;

public sealed class DirectoryRepository : IDirectoryRepository
{
    private readonly IDbConnectionFactory _connectionFactory;

    public DirectoryRepository(IDbConnectionFactory connectionFactory)
    {
        _connectionFactory = connectionFactory;
    }

    public async Task<IReadOnlyList<EtablissementSummary>> SearchAsync(string? categorie, string? commune, CancellationToken cancellationToken)
    {
        using var connection = _connectionFactory.CreateConnection();
        await connection.OpenAsync(cancellationToken);

        // INNER JOIN etablissement_categorie/categorie : garantit FR-002 (au moins une categorie)
        // sans logique applicative separee (voir research.md, Decision 1).
        var rows = await connection.QueryAsync<EtablissementSummary>(new CommandDefinition(
            @"SELECT DISTINCT e.id_etablissement AS IdEtablissement, e.nom_etablissement AS NomEtablissement,
                     c.libelle_commune AS LibelleCommune
              FROM etablissement e
              INNER JOIN etablissement_categorie ec ON ec.id_etablissement = e.id_etablissement
              INNER JOIN categorie cat ON cat.id_categorie = ec.id_categorie
              INNER JOIN commune c ON c.id_commune = e.id_commune
              WHERE e.statut_kyc = 'VALIDE' AND e.est_suspendu = false
                AND (@categorie IS NULL OR cat.libelle_categorie = @categorie)
                AND (@commune IS NULL OR c.libelle_commune = @commune)
              ORDER BY e.nom_etablissement;",
            new { categorie, commune },
            cancellationToken: cancellationToken));

        return rows.AsList();
    }

    public async Task<EtablissementCoreRow?> GetValidatedCoreAsync(Guid idEtablissement, CancellationToken cancellationToken)
    {
        using var connection = _connectionFactory.CreateConnection();
        await connection.OpenAsync(cancellationToken);

        return await connection.QuerySingleOrDefaultAsync<EtablissementCoreRow>(new CommandDefinition(
            @"SELECT id_etablissement AS IdEtablissement, nom_etablissement AS NomEtablissement,
                     description AS Description, numero_service_client AS NumeroServiceClient,
                     gps_latitude AS GpsLatitude, gps_longitude AS GpsLongitude
              FROM etablissement
              WHERE id_etablissement = @idEtablissement AND statut_kyc = 'VALIDE' AND est_suspendu = false;",
            new { idEtablissement },
            cancellationToken: cancellationToken));
    }

    public async Task<IReadOnlyList<MediaDto>> GetMediasAsync(Guid idEtablissement, CancellationToken cancellationToken)
    {
        using var connection = _connectionFactory.CreateConnection();
        await connection.OpenAsync(cancellationToken);

        var rows = await connection.QueryAsync<MediaDto>(new CommandDefinition(
            @"SELECT type_media AS TypeMedia, url AS Url, ordre_affichage AS OrdreAffichage
              FROM media WHERE id_etablissement = @idEtablissement ORDER BY ordre_affichage;",
            new { idEtablissement },
            cancellationToken: cancellationToken));

        return rows.AsList();
    }

    public async Task<IReadOnlyList<PrestationDto>> GetPrestationsAsync(Guid idEtablissement, CancellationToken cancellationToken)
    {
        using var connection = _connectionFactory.CreateConnection();
        await connection.OpenAsync(cancellationToken);

        var rows = await connection.QueryAsync<PrestationDto>(new CommandDefinition(
            @"SELECT id_prestation AS IdPrestation, libelle_prestation AS LibellePrestation, tarif AS Tarif, duree_minutes AS DureeMinutes
              FROM prestation WHERE id_etablissement = @idEtablissement ORDER BY libelle_prestation;",
            new { idEtablissement },
            cancellationToken: cancellationToken));

        return rows.AsList();
    }
}
