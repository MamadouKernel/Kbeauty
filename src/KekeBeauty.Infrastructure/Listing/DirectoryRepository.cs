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
                     c.libelle_commune AS LibelleCommune, e.gps_latitude AS GpsLatitude, e.gps_longitude AS GpsLongitude,
                     e.numero_service_client AS NumeroServiceClient, p.apercu AS PrestationsApercu
              FROM etablissement e
              INNER JOIN etablissement_categorie ec ON ec.id_etablissement = e.id_etablissement
              INNER JOIN categorie cat ON cat.id_categorie = ec.id_categorie
              INNER JOIN commune c ON c.id_commune = e.id_commune
              LEFT JOIN LATERAL (
                  SELECT string_agg(libelle_prestation, ', ') AS apercu
                  FROM (SELECT libelle_prestation FROM prestation WHERE id_etablissement = e.id_etablissement ORDER BY libelle_prestation LIMIT 3) top
              ) p ON true
              WHERE e.statut_kyc = 'VALIDE' AND e.est_suspendu = false
                AND (@categorie IS NULL OR
                     e.nom_etablissement ILIKE '%' || @categorie || '%' OR
                     cat.libelle_categorie ILIKE '%' || @categorie || '%' OR
                     EXISTS (SELECT 1 FROM prestation pr WHERE pr.id_etablissement = e.id_etablissement AND pr.libelle_prestation ILIKE '%' || @categorie || '%'))
                AND (@commune IS NULL OR c.libelle_commune ILIKE '%' || @commune || '%')
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
                     gps_latitude AS GpsLatitude, gps_longitude AS GpsLongitude,
                     CASE WHEN pf.paiement_mobile THEN mode_paiement_service ELSE 'ESPECES' END AS ModePaiementService,
                     (paiement_wave AND pf.paiement_mobile) AS PaiementWave,
                     (paiement_orange_money AND pf.paiement_mobile) AS PaiementOrangeMoney,
                     (paiement_moov_money AND pf.paiement_mobile) AS PaiementMoovMoney
              FROM etablissement
              JOIN parametre_formule pf ON pf.formule = CASE
                  WHEN EXISTS (SELECT 1 FROM abonnement a WHERE a.id_etablissement = etablissement.id_etablissement AND a.statut_abonnement = 'ACTIF') THEN 'PRO'
                  ELSE 'FREE' END
              WHERE id_etablissement = @idEtablissement AND statut_kyc = 'VALIDE' AND est_suspendu = false;",
            new { idEtablissement },
            cancellationToken: cancellationToken));
    }

    public async Task<EtablissementCoreRow?> GetManagedCoreAsync(Guid idEtablissement, CancellationToken cancellationToken)
    {
        using var connection = _connectionFactory.CreateConnection();
        await connection.OpenAsync(cancellationToken);

        return await connection.QuerySingleOrDefaultAsync<EtablissementCoreRow>(new CommandDefinition(
            @"SELECT id_etablissement AS IdEtablissement, nom_etablissement AS NomEtablissement,
                     description AS Description, numero_service_client AS NumeroServiceClient,
                     gps_latitude AS GpsLatitude, gps_longitude AS GpsLongitude,
                     statut_kyc AS StatutKyc, horaires #>> '{}' AS Horaires, motif_rejet AS MotifRejet,
                     CASE WHEN pf.paiement_mobile THEN mode_paiement_service ELSE 'ESPECES' END AS ModePaiementService,
                     (paiement_wave AND pf.paiement_mobile) AS PaiementWave,
                     (paiement_orange_money AND pf.paiement_mobile) AS PaiementOrangeMoney,
                     (paiement_moov_money AND pf.paiement_mobile) AS PaiementMoovMoney
              FROM etablissement
              JOIN parametre_formule pf ON pf.formule = CASE
                  WHEN EXISTS (SELECT 1 FROM abonnement a WHERE a.id_etablissement = etablissement.id_etablissement AND a.statut_abonnement = 'ACTIF') THEN 'PRO'
                  ELSE 'FREE' END
              WHERE id_etablissement = @idEtablissement AND est_suspendu = false;",
            new { idEtablissement }, cancellationToken: cancellationToken));
    }

    public async Task<IReadOnlyList<MediaDto>> GetMediasAsync(Guid idEtablissement, CancellationToken cancellationToken)
    {
        using var connection = _connectionFactory.CreateConnection();
        await connection.OpenAsync(cancellationToken);

        var rows = await connection.QueryAsync<MediaDto>(new CommandDefinition(
            @"SELECT id_media AS IdMedia, type_media AS TypeMedia, '/media/' || id_media AS Url, ordre_affichage AS OrdreAffichage
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
