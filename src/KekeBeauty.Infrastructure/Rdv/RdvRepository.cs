using Dapper;
using KekeBeauty.Application.Rdv;

namespace KekeBeauty.Infrastructure.Rdv;

public sealed class RdvRepository : IRdvRepository
{
    private readonly IDbConnectionFactory _connectionFactory;

    public RdvRepository(IDbConnectionFactory connectionFactory)
    {
        _connectionFactory = connectionFactory;
    }

    public async Task<IReadOnlyList<CreneauOccupe>> GetCreneauxOccupesAsync(Guid idEtablissement, DateOnly date, CancellationToken cancellationToken)
    {
        using var connection = _connectionFactory.CreateConnection();
        await connection.OpenAsync(cancellationToken);

        var jour = date.ToDateTime(TimeOnly.MinValue);
        var rows = await connection.QueryAsync<CreneauOccupe>(new CommandDefinition(
            @"SELECT r.date_heure_debut AS DateHeureDebut,
                     r.date_heure_debut + (p.duree_minutes * interval '1 minute') AS DateHeureFin
              FROM rdv r
              JOIN prestation p ON p.id_prestation = r.id_prestation
              WHERE r.id_etablissement = @idEtablissement
                AND r.statut_rdv IN ('DEMANDE', 'CONFIRME')
                AND r.date_heure_debut >= @jour AND r.date_heure_debut < @jour + interval '1 day'
              ORDER BY r.date_heure_debut;",
            new { idEtablissement, jour },
            cancellationToken: cancellationToken));

        return rows.AsList();
    }

    public async Task<Guid?> CreateIfNoOverlapAsync(
        Guid idEtablissement, Guid idPrestation, Guid idUtilisateurClient, DateTimeOffset dateHeureDebut, CancellationToken cancellationToken)
    {
        using var connection = _connectionFactory.CreateConnection();
        await connection.OpenAsync(cancellationToken);

        // Etablissement doit etre VALIDE et la prestation doit exister et lui appartenir.
        var prestationValid = await connection.ExecuteScalarAsync<int>(new CommandDefinition(
            @"SELECT count(*) FROM prestation p
              JOIN etablissement e ON e.id_etablissement = p.id_etablissement
              WHERE p.id_prestation = @idPrestation AND e.id_etablissement = @idEtablissement AND e.statut_kyc = 'VALIDE' AND e.est_suspendu = false;",
            new { idPrestation, idEtablissement },
            cancellationToken: cancellationToken));

        if (prestationValid == 0)
        {
            return null;
        }

        // Insertion atomique : n'insere que si aucun chevauchement n'existe (voir research.md Decision 1).
        // Le chevauchement compare l'intervalle du nouveau RDV a celui de chaque RDV existant (via la
        // duree de sa propre prestation).
        var idRdv = await connection.ExecuteScalarAsync<Guid?>(new CommandDefinition(
            @"INSERT INTO rdv (id_etablissement, id_prestation, id_utilisateur_client, date_heure_debut, statut_rdv)
              SELECT @idEtablissement, @idPrestation, @idUtilisateurClient, @dateHeureDebut, 'DEMANDE'
              WHERE NOT EXISTS (
                SELECT 1 FROM rdv r
                JOIN prestation p ON p.id_prestation = r.id_prestation
                JOIN prestation np ON np.id_prestation = @idPrestation
                WHERE r.id_etablissement = @idEtablissement
                  AND r.statut_rdv IN ('DEMANDE', 'CONFIRME')
                  AND @dateHeureDebut < r.date_heure_debut + (p.duree_minutes * interval '1 minute')
                  AND r.date_heure_debut < @dateHeureDebut + (np.duree_minutes * interval '1 minute')
              )
              RETURNING id_rdv;",
            new { idEtablissement, idPrestation, idUtilisateurClient, dateHeureDebut },
            cancellationToken: cancellationToken));

        return idRdv ?? Guid.Empty;
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

    public async Task<string?> GetClientTelephoneAsync(Guid idRdv, CancellationToken cancellationToken)
    {
        using var connection = _connectionFactory.CreateConnection();
        await connection.OpenAsync(cancellationToken);

        return await connection.ExecuteScalarAsync<string?>(new CommandDefinition(
            @"SELECT u.telephone FROM rdv r JOIN utilisateur u ON u.id_utilisateur = r.id_utilisateur_client
              WHERE r.id_rdv = @idRdv;",
            new { idRdv },
            cancellationToken: cancellationToken));
    }

    public async Task<bool> UpdateStatutAsync(Guid idEtablissement, Guid idRdv, string statutRdv, CancellationToken cancellationToken)
    {
        using var connection = _connectionFactory.CreateConnection();
        await connection.OpenAsync(cancellationToken);

        var rows = await connection.ExecuteAsync(new CommandDefinition(
            @"UPDATE rdv SET statut_rdv = @statutRdv::statut_rdv_enum
              WHERE id_rdv = @idRdv AND id_etablissement = @idEtablissement;",
            new { statutRdv, idRdv, idEtablissement },
            cancellationToken: cancellationToken));

        return rows > 0;
    }

    public async Task<bool> RescheduleAsync(Guid idEtablissement, Guid idRdv, DateTimeOffset nouvelleDateHeureDebut, CancellationToken cancellationToken)
    {
        using var connection = _connectionFactory.CreateConnection();
        await connection.OpenAsync(cancellationToken);

        var rows = await connection.ExecuteAsync(new CommandDefinition(
            @"UPDATE rdv r SET date_heure_debut = @nouvelleDateHeureDebut
              WHERE r.id_rdv = @idRdv AND r.id_etablissement = @idEtablissement
                AND NOT EXISTS (
                  SELECT 1 FROM rdv other
                  JOIN prestation op ON op.id_prestation = other.id_prestation
                  JOIN prestation np ON np.id_prestation = r.id_prestation
                  WHERE other.id_etablissement = r.id_etablissement
                    AND other.id_rdv <> r.id_rdv
                    AND other.statut_rdv IN ('DEMANDE', 'CONFIRME')
                    AND @nouvelleDateHeureDebut < other.date_heure_debut + (op.duree_minutes * interval '1 minute')
                    AND other.date_heure_debut < @nouvelleDateHeureDebut + (np.duree_minutes * interval '1 minute')
                );",
            new { idEtablissement, idRdv, nouvelleDateHeureDebut },
            cancellationToken: cancellationToken));

        return rows > 0;
    }
}
