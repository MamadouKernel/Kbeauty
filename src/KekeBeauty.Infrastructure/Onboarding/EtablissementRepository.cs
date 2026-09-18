using Dapper;
using KekeBeauty.Application.Onboarding;

namespace KekeBeauty.Infrastructure.Onboarding;

public sealed class EtablissementRepository : IEtablissementRepository
{
    // Seed geographique minimal cree par la migration 0004_geo_seed_minimal.sql
    // (voir specs/004-onboarding-partenaire/research.md, Decision 3).
    private static readonly Guid SeedCommuneId = Guid.Parse("00000000-0000-0000-0000-000000000004");

    private readonly IDbConnectionFactory _connectionFactory;

    public EtablissementRepository(IDbConnectionFactory connectionFactory)
    {
        _connectionFactory = connectionFactory;
    }

    public async Task<Guid> CreateAsync(NewEtablissement etablissement, CancellationToken cancellationToken)
    {
        using var connection = _connectionFactory.CreateConnection();
        await connection.OpenAsync(cancellationToken);

        return await connection.ExecuteScalarAsync<Guid>(new CommandDefinition(
            @"INSERT INTO etablissement
                (id_etablissement, nom_etablissement, gps_latitude, gps_longitude, numero_service_client,
                 horaires, statut_kyc, url_photo_devanture, url_piece_identite, id_utilisateur_gerant, id_commune)
              VALUES
                (@IdEtablissement, @NomEtablissement, @GpsLatitude, @GpsLongitude, @NumeroServiceClient,
                 @Horaires::jsonb, 'EN_ATTENTE', @UrlPhotoDevanture, @UrlPieceIdentite, @IdUtilisateurGerant, @IdCommune)
              RETURNING id_etablissement;",
            new
            {
                etablissement.IdEtablissement,
                etablissement.NomEtablissement,
                etablissement.GpsLatitude,
                etablissement.GpsLongitude,
                etablissement.NumeroServiceClient,
                etablissement.Horaires,
                etablissement.UrlPhotoDevanture,
                etablissement.UrlPieceIdentite,
                etablissement.IdUtilisateurGerant,
                IdCommune = SeedCommuneId,
            },
            cancellationToken: cancellationToken));
    }

    public async Task<IReadOnlyList<ApplicationSummary>> ListByStatutAsync(string statutKyc, CancellationToken cancellationToken)
    {
        using var connection = _connectionFactory.CreateConnection();
        await connection.OpenAsync(cancellationToken);

        var rows = await connection.QueryAsync<ApplicationSummary>(new CommandDefinition(
            @"SELECT id_etablissement AS IdEtablissement, nom_etablissement AS NomEtablissement,
                     date_creation AS DateCreation, statut_kyc AS StatutKyc
              FROM etablissement
              WHERE statut_kyc = @statutKyc::statut_kyc_enum
              ORDER BY date_creation;",
            new { statutKyc },
            cancellationToken: cancellationToken));

        return rows.AsList();
    }

    public async Task<ApplicationDetail?> GetByIdAsync(Guid idEtablissement, CancellationToken cancellationToken)
    {
        using var connection = _connectionFactory.CreateConnection();
        await connection.OpenAsync(cancellationToken);

        return await connection.QuerySingleOrDefaultAsync<ApplicationDetail>(new CommandDefinition(
            @"SELECT id_etablissement AS IdEtablissement, nom_etablissement AS NomEtablissement,
                     gps_latitude AS GpsLatitude, gps_longitude AS GpsLongitude,
                     numero_service_client AS NumeroServiceClient, statut_kyc AS StatutKyc,
                     (url_photo_devanture IS NOT NULL) AS HasPhotoDevanture,
                     (url_piece_identite IS NOT NULL) AS HasPieceIdentite,
                     date_creation AS DateCreation
              FROM etablissement
              WHERE id_etablissement = @idEtablissement;",
            new { idEtablissement },
            cancellationToken: cancellationToken));
    }

    public async Task UpdateStatutAsync(Guid idEtablissement, string statutKyc, CancellationToken cancellationToken)
    {
        using var connection = _connectionFactory.CreateConnection();
        await connection.OpenAsync(cancellationToken);

        await connection.ExecuteAsync(new CommandDefinition(
            "UPDATE etablissement SET statut_kyc = @statutKyc::statut_kyc_enum WHERE id_etablissement = @idEtablissement;",
            new { idEtablissement, statutKyc },
            cancellationToken: cancellationToken));
    }

    public async Task<string?> GetFilePathAsync(Guid idEtablissement, string fileType, CancellationToken cancellationToken)
    {
        using var connection = _connectionFactory.CreateConnection();
        await connection.OpenAsync(cancellationToken);

        // fileType provient d'un parametre de route utilisateur : jamais interpole dans le SQL,
        // toujours passe comme parametre lie a une expression CASE fixe.
        return await connection.ExecuteScalarAsync<string?>(new CommandDefinition(
            @"SELECT CASE WHEN @fileType = 'piece-identite' THEN url_piece_identite ELSE url_photo_devanture END
              FROM etablissement WHERE id_etablissement = @idEtablissement;",
            new { idEtablissement, fileType },
            cancellationToken: cancellationToken));
    }

    public async Task<string?> GetGerantTelephoneAsync(Guid idEtablissement, CancellationToken cancellationToken)
    {
        using var connection = _connectionFactory.CreateConnection();
        await connection.OpenAsync(cancellationToken);

        return await connection.ExecuteScalarAsync<string?>(new CommandDefinition(
            @"SELECT u.telephone
              FROM etablissement e
              JOIN utilisateur u ON u.id_utilisateur = e.id_utilisateur_gerant
              WHERE e.id_etablissement = @idEtablissement;",
            new { idEtablissement },
            cancellationToken: cancellationToken));
    }
}
