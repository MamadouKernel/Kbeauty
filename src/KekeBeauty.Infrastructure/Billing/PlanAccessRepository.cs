using Dapper;
using KekeBeauty.Application.Billing;

namespace KekeBeauty.Infrastructure.Billing;

public sealed class PlanAccessRepository : IPlanAccessRepository
{
    private readonly IDbConnectionFactory _connectionFactory;

    public PlanAccessRepository(IDbConnectionFactory connectionFactory) => _connectionFactory = connectionFactory;

    public async Task<bool> IsProAsync(Guid idEtablissement, CancellationToken cancellationToken)
    {
        using var connection = _connectionFactory.CreateConnection();
        await connection.OpenAsync(cancellationToken);
        return await connection.ExecuteScalarAsync<bool>(new CommandDefinition(
            @"SELECT EXISTS (SELECT 1 FROM abonnement
              WHERE id_etablissement = @idEtablissement AND statut_abonnement = 'ACTIF');",
            new { idEtablissement }, cancellationToken: cancellationToken));
    }

    public async Task<PlanEntitlements?> GetEntitlementsAsync(string formule, CancellationToken cancellationToken)
    {
        using var connection = _connectionFactory.CreateConnection();
        await connection.OpenAsync(cancellationToken);
        return await connection.QuerySingleOrDefaultAsync<PlanEntitlements>(new CommandDefinition(
            @"SELECT formule AS Formule, limite_prestations AS LimitePrestations,
                     limite_rdv_mensuels AS LimiteRdvMensuels, paiement_mobile AS PaiementMobile,
                     gestion_equipe AS GestionEquipe, statistiques_avancees AS StatistiquesAvancees
              FROM parametre_formule WHERE formule = @formule;",
            new { formule }, cancellationToken: cancellationToken));
    }

    public async Task<IReadOnlyList<PlanEntitlements>> ListEntitlementsAsync(CancellationToken cancellationToken)
    {
        using var connection = _connectionFactory.CreateConnection();
        await connection.OpenAsync(cancellationToken);
        var rows = await connection.QueryAsync<PlanEntitlements>(new CommandDefinition(
            @"SELECT formule AS Formule, limite_prestations AS LimitePrestations,
                     limite_rdv_mensuels AS LimiteRdvMensuels, paiement_mobile AS PaiementMobile,
                     gestion_equipe AS GestionEquipe, statistiques_avancees AS StatistiquesAvancees
              FROM parametre_formule ORDER BY formule;", cancellationToken: cancellationToken));
        return rows.AsList();
    }

    public async Task<bool> UpdateEntitlementsAsync(PlanEntitlements entitlements, CancellationToken cancellationToken)
    {
        using var connection = _connectionFactory.CreateConnection();
        await connection.OpenAsync(cancellationToken);
        var rows = await connection.ExecuteAsync(new CommandDefinition(
            @"UPDATE parametre_formule SET limite_prestations = @LimitePrestations,
                     limite_rdv_mensuels = @LimiteRdvMensuels, paiement_mobile = @PaiementMobile,
                     gestion_equipe = @GestionEquipe, statistiques_avancees = @StatistiquesAvancees,
                     date_modification = now()
              WHERE formule = @Formule;", entitlements, cancellationToken: cancellationToken));
        return rows > 0;
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
