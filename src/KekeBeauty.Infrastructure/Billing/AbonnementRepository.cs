using Dapper;
using KekeBeauty.Application.Billing;

namespace KekeBeauty.Infrastructure.Billing;

public sealed class AbonnementRepository : IAbonnementRepository
{
    private readonly IDbConnectionFactory _connectionFactory;

    public AbonnementRepository(IDbConnectionFactory connectionFactory)
    {
        _connectionFactory = connectionFactory;
    }

    public async Task<decimal> GetTarifStandardAsync(string periodicite, CancellationToken cancellationToken)
    {
        using var connection = _connectionFactory.CreateConnection();
        await connection.OpenAsync(cancellationToken);

        return await connection.ExecuteScalarAsync<decimal>(new CommandDefinition(
            "SELECT montant FROM parametre_abonnement WHERE periodicite = @periodicite::periodicite_enum;",
            new { periodicite },
            cancellationToken: cancellationToken));
    }

    public async Task<Guid?> CreerAvecTransactionAsync(
        Guid idEtablissement, string periodicite, decimal montant, string referenceExterne,
        CancellationToken cancellationToken)
    {
        using var connection = _connectionFactory.CreateConnection();
        await connection.OpenAsync(cancellationToken);

        // Insertion atomique : n'insere que si aucun abonnement ACTIF n'existe deja pour cet
        // etablissement (voir research.md Decision 2, meme pattern que le chevauchement RDV).
        // L'abonnement demarre IMPAYE : le paiement reel arrive plus tard via callback WiniPayer
        // (MarquerPaiementAsync), le checkout WiniPayer etant heberge et asynchrone.
        var idAbonnement = await connection.ExecuteScalarAsync<Guid?>(new CommandDefinition(
            @"INSERT INTO abonnement (id_etablissement, periodicite, date_debut_engagement, montant, statut_abonnement)
              SELECT @idEtablissement, @periodicite::periodicite_enum, CURRENT_DATE, @montant, 'IMPAYE'::statut_abonnement_enum
              WHERE EXISTS (SELECT 1 FROM etablissement WHERE id_etablissement = @idEtablissement AND est_suspendu = false)
                AND NOT EXISTS (
                SELECT 1 FROM abonnement WHERE id_etablissement = @idEtablissement AND statut_abonnement = 'ACTIF'
              )
              RETURNING id_abonnement;",
            new { idEtablissement, periodicite, montant },
            cancellationToken: cancellationToken));

        if (idAbonnement is null)
        {
            return null;
        }

        await connection.ExecuteAsync(new CommandDefinition(
            @"INSERT INTO transaction (id_abonnement, statut_transaction, montant, reference_externe)
              VALUES (@idAbonnement, 'EN_COURS'::statut_transaction_enum, @montant, @referenceExterne);",
            new { idAbonnement, montant, referenceExterne },
            cancellationToken: cancellationToken));

        return idAbonnement;
    }

    public async Task<bool> MarquerPaiementAsync(string referenceExterne, bool paiementReussi, string? operateurExterne, CancellationToken cancellationToken)
    {
        using var connection = _connectionFactory.CreateConnection();
        await connection.OpenAsync(cancellationToken);

        var statutTransaction = paiementReussi ? "REUSSIE" : "ECHOUEE";

        // Idempotent : seule une transaction encore EN_COURS est mise a jour, un callback rejoue
        // n'a donc aucun effet supplementaire.
        var idAbonnement = await connection.ExecuteScalarAsync<Guid?>(new CommandDefinition(
            @"UPDATE transaction SET statut_transaction = @statutTransaction::statut_transaction_enum, operateur_externe = @operateurExterne
              WHERE reference_externe = @referenceExterne AND statut_transaction = 'EN_COURS'
              RETURNING id_abonnement;",
            new { referenceExterne, statutTransaction, operateurExterne },
            cancellationToken: cancellationToken));

        if (idAbonnement is null)
        {
            return false;
        }

        if (paiementReussi)
        {
            await connection.ExecuteAsync(new CommandDefinition(
                "UPDATE abonnement SET statut_abonnement = 'ACTIF' WHERE id_abonnement = @idAbonnement;",
                new { idAbonnement },
                cancellationToken: cancellationToken));
        }

        return true;
    }

    public async Task<IReadOnlyList<AbonnementResume>> ListerAsync(string? statut, CancellationToken cancellationToken)
    {
        using var connection = _connectionFactory.CreateConnection();
        await connection.OpenAsync(cancellationToken);

        var rows = await connection.QueryAsync<AbonnementResume>(new CommandDefinition(
            @"SELECT id_abonnement AS IdAbonnement, id_etablissement AS IdEtablissement, periodicite AS Periodicite,
                     montant AS Montant, statut_abonnement AS StatutAbonnement
              FROM abonnement
              WHERE @statut IS NULL OR statut_abonnement = @statut::statut_abonnement_enum
              ORDER BY id_abonnement;",
            new { statut },
            cancellationToken: cancellationToken));

        return rows.AsList();
    }

    public async Task<AbonnementResume?> GetByIdAsync(Guid idAbonnement, CancellationToken cancellationToken)
    {
        using var connection = _connectionFactory.CreateConnection();
        await connection.OpenAsync(cancellationToken);

        return await connection.QuerySingleOrDefaultAsync<AbonnementResume>(new CommandDefinition(
            @"SELECT id_abonnement AS IdAbonnement, id_etablissement AS IdEtablissement, periodicite AS Periodicite,
                     montant AS Montant, statut_abonnement AS StatutAbonnement
              FROM abonnement WHERE id_abonnement = @idAbonnement;",
            new { idAbonnement },
            cancellationToken: cancellationToken));
    }

    public async Task<string?> GetGerantTelephoneAsync(Guid idAbonnement, CancellationToken cancellationToken)
    {
        using var connection = _connectionFactory.CreateConnection();
        await connection.OpenAsync(cancellationToken);

        return await connection.ExecuteScalarAsync<string?>(new CommandDefinition(
            @"SELECT u.telephone FROM abonnement a
              JOIN etablissement e ON e.id_etablissement = a.id_etablissement
              JOIN utilisateur u ON u.id_utilisateur = e.id_utilisateur_gerant
              WHERE a.id_abonnement = @idAbonnement;",
            new { idAbonnement },
            cancellationToken: cancellationToken));
    }

    public async Task<IReadOnlyList<TarifStandard>> ListerTarifsAsync(CancellationToken cancellationToken)
    {
        using var connection = _connectionFactory.CreateConnection();
        await connection.OpenAsync(cancellationToken);

        var rows = await connection.QueryAsync<TarifStandard>(new CommandDefinition(
            "SELECT periodicite AS Periodicite, montant AS Montant FROM parametre_abonnement ORDER BY periodicite;",
            cancellationToken: cancellationToken));

        return rows.AsList();
    }

    public async Task SetTarifStandardAsync(string periodicite, decimal montant, CancellationToken cancellationToken)
    {
        using var connection = _connectionFactory.CreateConnection();
        await connection.OpenAsync(cancellationToken);

        await connection.ExecuteAsync(new CommandDefinition(
            "UPDATE parametre_abonnement SET montant = @montant WHERE periodicite = @periodicite::periodicite_enum;",
            new { periodicite, montant },
            cancellationToken: cancellationToken));
    }
}
