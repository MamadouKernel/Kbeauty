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
        using var transaction = connection.BeginTransaction();

        var idEtablissement = await connection.ExecuteScalarAsync<Guid>(new CommandDefinition(
            @"INSERT INTO etablissement
                (id_etablissement, nom_etablissement, gps_latitude, gps_longitude, numero_service_client,
                 horaires, statut_kyc, url_photo_devanture, type_document_identite, url_document_recto, url_document_verso, id_utilisateur_gerant, id_commune,
                 mode_paiement_service, paiement_wave, paiement_orange_money, paiement_moov_money)
              VALUES
                (@IdEtablissement, @NomEtablissement, @GpsLatitude, @GpsLongitude, @NumeroServiceClient,
                 @Horaires::jsonb, 'EN_ATTENTE', @UrlPhotoDevanture, @TypeDocumentIdentite, @UrlDocumentRecto, @UrlDocumentVerso, @IdUtilisateurGerant, @IdCommune,
                 @ModePaiementService, @PaiementWave, @PaiementOrangeMoney, @PaiementMoovMoney)
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
                etablissement.TypeDocumentIdentite,
                etablissement.UrlDocumentRecto,
                etablissement.UrlDocumentVerso,
                etablissement.IdUtilisateurGerant,
                etablissement.ModePaiementService,
                etablissement.PaiementWave,
                etablissement.PaiementOrangeMoney,
                etablissement.PaiementMoovMoney,
                IdCommune = SeedCommuneId,
            },
            transaction: transaction,
            cancellationToken: cancellationToken));

        if (!string.IsNullOrWhiteSpace(etablissement.Categorie))
        {
            var idCategorie = await connection.ExecuteScalarAsync<Guid>(new CommandDefinition(
                @"INSERT INTO categorie (libelle_categorie)
                  VALUES (@Categorie)
                  ON CONFLICT (libelle_categorie) DO UPDATE SET libelle_categorie = EXCLUDED.libelle_categorie
                  RETURNING id_categorie;",
                new { Categorie = etablissement.Categorie.Trim() }, transaction,
                cancellationToken: cancellationToken));

            await connection.ExecuteAsync(new CommandDefinition(
                @"INSERT INTO etablissement_categorie (id_etablissement, id_categorie)
                  VALUES (@IdEtablissement, @IdCategorie)
                  ON CONFLICT DO NOTHING;",
                new { IdEtablissement = idEtablissement, IdCategorie = idCategorie }, transaction,
                cancellationToken: cancellationToken));
        }

        transaction.Commit();
        return idEtablissement;
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
                     type_document_identite AS TypeDocumentIdentite,
                     (url_document_recto IS NOT NULL) AS HasDocumentRecto,
                     (url_document_verso IS NOT NULL) AS HasDocumentVerso,
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

    public async Task RejectAsync(Guid idEtablissement, string motif, CancellationToken cancellationToken)
    {
        using var connection = _connectionFactory.CreateConnection(); await connection.OpenAsync(cancellationToken);
        await connection.ExecuteAsync(new CommandDefinition(
            "UPDATE etablissement SET statut_kyc='REJETE', motif_rejet=@motif WHERE id_etablissement=@idEtablissement;",
            new { idEtablissement, motif }, cancellationToken: cancellationToken));
    }

    public async Task<bool> ResubmitAsync(Guid idEtablissement, string? photoPath, string? documentRectoPath, string? documentVersoPath, string? typeDocumentIdentite, CancellationToken ct)
    {
        using var c = _connectionFactory.CreateConnection(); await c.OpenAsync(ct);
        return await c.ExecuteAsync(new CommandDefinition(@"UPDATE etablissement SET
            url_photo_devanture=COALESCE(@photoPath,url_photo_devanture),
            url_document_recto=COALESCE(@documentRectoPath,url_document_recto),
            url_piece_identite=COALESCE(@documentRectoPath,url_piece_identite),
            url_document_verso=CASE WHEN @typeDocumentIdentite='PASSEPORT' THEN NULL ELSE COALESCE(@documentVersoPath,url_document_verso) END,
            type_document_identite=COALESCE(@typeDocumentIdentite,type_document_identite),
            statut_kyc='EN_ATTENTE',motif_rejet=NULL,date_soumission_kyc=now()
            WHERE id_etablissement=@idEtablissement AND statut_kyc='REJETE';",
            new{idEtablissement,photoPath,documentRectoPath,documentVersoPath,typeDocumentIdentite},cancellationToken:ct))>0;
    }
    public async Task<string?> GetFilePathAsync(Guid idEtablissement, string fileType, CancellationToken cancellationToken)
    {
        using var connection = _connectionFactory.CreateConnection();
        await connection.OpenAsync(cancellationToken);

        // fileType provient d'un parametre de route utilisateur : jamais interpole dans le SQL,
        // toujours passe comme parametre lie a une expression CASE fixe.
        return await connection.ExecuteScalarAsync<string?>(new CommandDefinition(
            @"SELECT CASE WHEN @fileType = 'document-recto' THEN url_document_recto WHEN @fileType = 'document-verso' THEN url_document_verso WHEN @fileType = 'piece-identite' THEN COALESCE(url_document_recto,url_piece_identite) ELSE url_photo_devanture END
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
