using Dapper;
using KekeBeauty.Application.Billing;
using Npgsql;

namespace KekeBeauty.Infrastructure.Billing;

public sealed class PlanAccessRepository : IPlanAccessRepository
{
    private readonly IDbConnectionFactory _connectionFactory;

    public PlanAccessRepository(IDbConnectionFactory connectionFactory) => _connectionFactory = connectionFactory;

    public async Task<string> GetFormuleActuelleAsync(Guid idEtablissement, CancellationToken cancellationToken)
    {
        using var connection = _connectionFactory.CreateConnection();
        await connection.OpenAsync(cancellationToken);
        return await connection.ExecuteScalarAsync<string>(new CommandDefinition(
            @"SELECT COALESCE(
                  (SELECT formule FROM abonnement
                   WHERE id_etablissement = @idEtablissement AND statut_abonnement = 'ACTIF'
                   ORDER BY date_debut_engagement DESC LIMIT 1),
                  (SELECT formule FROM parametre_formule WHERE est_defaut LIMIT 1)
              );",
            new { idEtablissement }, cancellationToken: cancellationToken));
    }

    private const string EntitlementColumns =
        @"formule AS Formule, libelle AS Libelle, est_actif AS EstActif, est_defaut AS EstDefaut,
          ordre_affichage AS OrdreAffichage, tarif_mensuel AS TarifMensuel, tarif_annuel AS TarifAnnuel,
          limite_prestations AS LimitePrestations, limite_rdv_mensuels AS LimiteRdvMensuels,
          paiement_mobile AS PaiementMobile, gestion_equipe AS GestionEquipe,
          statistiques_avancees AS StatistiquesAvancees, promo_pourcentage AS PromoPourcentage, promo_fin AS PromoFin";

    public async Task<PlanEntitlements?> GetEntitlementsAsync(string formule, CancellationToken cancellationToken)
    {
        using var connection = _connectionFactory.CreateConnection();
        await connection.OpenAsync(cancellationToken);
        var entitlement = await connection.QuerySingleOrDefaultAsync<PlanEntitlements>(new CommandDefinition(
            $"SELECT {EntitlementColumns} FROM parametre_formule WHERE formule = @formule;",
            new { formule }, cancellationToken: cancellationToken));
        if (entitlement is not null)
        {
            entitlement.Avantages = await GetAvantagesAsync(connection, formule, cancellationToken);
        }
        return entitlement;
    }

    public async Task<IReadOnlyList<PlanEntitlements>> ListEntitlementsAsync(CancellationToken cancellationToken)
    {
        using var connection = _connectionFactory.CreateConnection();
        await connection.OpenAsync(cancellationToken);
        var rows = (await connection.QueryAsync<PlanEntitlements>(new CommandDefinition(
            $"SELECT {EntitlementColumns} FROM parametre_formule ORDER BY ordre_affichage, formule;",
            cancellationToken: cancellationToken))).AsList();

        var avantages = (await connection.QueryAsync<(string Formule, string Libelle)>(new CommandDefinition(
            "SELECT formule AS Formule, libelle AS Libelle FROM parametre_formule_avantage ORDER BY formule, ordre_affichage;",
            cancellationToken: cancellationToken))).ToLookup(x => x.Formule, x => x.Libelle);
        foreach (var row in rows)
        {
            row.Avantages = avantages[row.Formule].ToList();
        }
        return rows;
    }

    private static async Task<List<string>> GetAvantagesAsync(System.Data.IDbConnection connection, string formule, CancellationToken cancellationToken)
    {
        var rows = await connection.QueryAsync<string>(new CommandDefinition(
            "SELECT libelle FROM parametre_formule_avantage WHERE formule = @formule ORDER BY ordre_affichage;",
            new { formule }, cancellationToken: cancellationToken));
        return rows.AsList();
    }

    private static async Task SetAvantagesAsync(
        Npgsql.NpgsqlConnection connection, Npgsql.NpgsqlTransaction transaction, string formule, IReadOnlyList<string> avantages, CancellationToken cancellationToken)
    {
        await connection.ExecuteAsync(new CommandDefinition(
            "DELETE FROM parametre_formule_avantage WHERE formule = @formule;",
            new { formule }, transaction, cancellationToken: cancellationToken));
        if (avantages.Count == 0)
        {
            return;
        }
        var rows = avantages.Select((libelle, index) => new { formule, libelle, ordre = index });
        await connection.ExecuteAsync(new CommandDefinition(
            "INSERT INTO parametre_formule_avantage (formule, libelle, ordre_affichage) VALUES (@formule, @libelle, @ordre);",
            rows, transaction, cancellationToken: cancellationToken));
    }

    public async Task<bool> UpdateEntitlementsAsync(PlanEntitlements entitlements, CancellationToken cancellationToken)
    {
        using var connection = _connectionFactory.CreateConnection();
        await connection.OpenAsync(cancellationToken);
        using var transaction = await connection.BeginTransactionAsync(cancellationToken);

        var rows = await connection.ExecuteAsync(new CommandDefinition(
            @"UPDATE parametre_formule SET libelle = @Libelle, est_actif = @EstActif,
                     ordre_affichage = @OrdreAffichage, tarif_mensuel = @TarifMensuel, tarif_annuel = @TarifAnnuel,
                     limite_prestations = @LimitePrestations, limite_rdv_mensuels = @LimiteRdvMensuels,
                     paiement_mobile = @PaiementMobile, gestion_equipe = @GestionEquipe,
                     statistiques_avancees = @StatistiquesAvancees, promo_pourcentage = @PromoPourcentage,
                     promo_fin = @PromoFin, date_modification = now()
              WHERE formule = @Formule;", entitlements, transaction, cancellationToken: cancellationToken));
        if (rows == 0)
        {
            await transaction.RollbackAsync(cancellationToken);
            return false;
        }

        await SetAvantagesAsync(connection, transaction, entitlements.Formule, entitlements.Avantages, cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return true;
    }

    public async Task<bool> CreateEntitlementsAsync(PlanEntitlements entitlements, CancellationToken cancellationToken)
    {
        using var connection = _connectionFactory.CreateConnection();
        await connection.OpenAsync(cancellationToken);
        using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        try
        {
            var rows = await connection.ExecuteAsync(new CommandDefinition(
                @"INSERT INTO parametre_formule
                      (formule, libelle, est_actif, est_defaut, ordre_affichage, tarif_mensuel, tarif_annuel,
                       limite_prestations, limite_rdv_mensuels, paiement_mobile, gestion_equipe, statistiques_avancees,
                       promo_pourcentage, promo_fin)
                  VALUES
                      (@Formule, @Libelle, @EstActif, FALSE, @OrdreAffichage, @TarifMensuel, @TarifAnnuel,
                       @LimitePrestations, @LimiteRdvMensuels, @PaiementMobile, @GestionEquipe, @StatistiquesAvancees,
                       @PromoPourcentage, @PromoFin);",
                entitlements, transaction, cancellationToken: cancellationToken));
            if (rows == 0)
            {
                await transaction.RollbackAsync(cancellationToken);
                return false;
            }
            await SetAvantagesAsync(connection, transaction, entitlements.Formule, entitlements.Avantages, cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return true;
        }
        catch (PostgresException ex) when (ex.SqlState == "23505")
        {
            await transaction.RollbackAsync(cancellationToken);
            return false;
        }
    }

    public async Task<DeleteFormuleResult> DeleteEntitlementsAsync(string formule, CancellationToken cancellationToken)
    {
        using var connection = _connectionFactory.CreateConnection();
        await connection.OpenAsync(cancellationToken);

        var estDefaut = await connection.QuerySingleOrDefaultAsync<bool?>(new CommandDefinition(
            "SELECT est_defaut FROM parametre_formule WHERE formule = @formule;",
            new { formule }, cancellationToken: cancellationToken));
        if (estDefaut is null)
        {
            return DeleteFormuleResult.NotFound;
        }
        if (estDefaut.Value)
        {
            return DeleteFormuleResult.EstFormuleParDefaut;
        }

        var nombreFormules = await connection.ExecuteScalarAsync<int>(new CommandDefinition(
            "SELECT COUNT(*) FROM parametre_formule;", cancellationToken: cancellationToken));
        if (nombreFormules <= 1)
        {
            return DeleteFormuleResult.DerniereFormule;
        }

        try
        {
            var rows = await connection.ExecuteAsync(new CommandDefinition(
                "DELETE FROM parametre_formule WHERE formule = @formule;",
                new { formule }, cancellationToken: cancellationToken));
            return rows > 0 ? DeleteFormuleResult.Deleted : DeleteFormuleResult.NotFound;
        }
        catch (PostgresException ex) when (ex.SqlState == "23503")
        {
            return DeleteFormuleResult.FormuleUtilisee;
        }
    }

    public async Task<bool> SetDefaultAsync(string formule, CancellationToken cancellationToken)
    {
        using var connection = _connectionFactory.CreateConnection();
        await connection.OpenAsync(cancellationToken);
        using var transaction = await connection.BeginTransactionAsync(cancellationToken);

        var existe = await connection.ExecuteScalarAsync<bool>(new CommandDefinition(
            "SELECT EXISTS (SELECT 1 FROM parametre_formule WHERE formule = @formule AND est_actif);",
            new { formule }, transaction, cancellationToken: cancellationToken));
        if (!existe)
        {
            return false;
        }

        await connection.ExecuteAsync(new CommandDefinition(
            "UPDATE parametre_formule SET est_defaut = FALSE WHERE est_defaut;",
            transaction: transaction, cancellationToken: cancellationToken));
        await connection.ExecuteAsync(new CommandDefinition(
            "UPDATE parametre_formule SET est_defaut = TRUE WHERE formule = @formule;",
            new { formule }, transaction, cancellationToken: cancellationToken));

        await transaction.CommitAsync(cancellationToken);
        return true;
    }

    public async Task<int> CountPrestationsAsync(Guid idEtablissement, CancellationToken cancellationToken)
    {
        using var connection = _connectionFactory.CreateConnection();
        await connection.OpenAsync(cancellationToken);
        return await connection.ExecuteScalarAsync<int>(new CommandDefinition(
            "SELECT COUNT(*) FROM prestation WHERE id_etablissement = @idEtablissement;",
            new { idEtablissement }, cancellationToken: cancellationToken));
    }

    public async Task<int> CountRendezVousCurrentMonthAsync(Guid idEtablissement, CancellationToken cancellationToken)
    {
        using var connection = _connectionFactory.CreateConnection();
        await connection.OpenAsync(cancellationToken);
        return await connection.ExecuteScalarAsync<int>(new CommandDefinition(
            @"SELECT COUNT(*) FROM rdv WHERE id_etablissement = @idEtablissement
              AND date_creation >= date_trunc('month', CURRENT_TIMESTAMP)
              AND date_creation < date_trunc('month', CURRENT_TIMESTAMP) + interval '1 month';",
            new { idEtablissement }, cancellationToken: cancellationToken));
    }
}
