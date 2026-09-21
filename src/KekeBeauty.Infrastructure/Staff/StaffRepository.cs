using Dapper;
using KekeBeauty.Application.Staff;

namespace KekeBeauty.Infrastructure.Staff;

public sealed class StaffRepository : IStaffRepository
{
    private readonly IDbConnectionFactory _connectionFactory;

    public StaffRepository(IDbConnectionFactory connectionFactory)
    {
        _connectionFactory = connectionFactory;
    }

    public async Task<Guid?> GetIdCollaborateurByUtilisateurAsync(Guid idUtilisateur, CancellationToken cancellationToken)
    {
        using var connection = _connectionFactory.CreateConnection();
        await connection.OpenAsync(cancellationToken);

        return await connection.ExecuteScalarAsync<Guid?>(new CommandDefinition(
            "SELECT id_collaborateur FROM collaborateur WHERE id_utilisateur = @idUtilisateur;",
            new { idUtilisateur },
            cancellationToken: cancellationToken));
    }

    public async Task<IReadOnlyList<PlanningItem>> GetPlanningAsync(Guid idCollaborateur, CancellationToken cancellationToken)
    {
        using var connection = _connectionFactory.CreateConnection();
        await connection.OpenAsync(cancellationToken);

        var rows = await connection.QueryAsync<PlanningItem>(new CommandDefinition(
            @"SELECT r.id_rdv AS IdRdv, r.date_heure_debut AS DateHeureDebut, r.statut_rdv AS StatutRdv,
                     p.libelle_prestation AS LibellePrestation, e.nom_etablissement AS NomEtablissement, u.nom AS NomClient, p.duree_minutes AS DureeMinutes,
                     r.date_debut_reelle AS DateDebutReelle, r.date_pause AS DatePause, r.duree_pause_secondes AS DureePauseSecondes, r.date_fin_reelle AS DateFinReelle, r.retard_minutes AS RetardMinutes, r.date_signalement_retard AS DateSignalementRetard
              FROM rdv r
              JOIN prestation p ON p.id_prestation = r.id_prestation
              JOIN etablissement e ON e.id_etablissement = r.id_etablissement
              WHERE r.id_collaborateur = @idCollaborateur
              ORDER BY r.date_heure_debut DESC;",
            new { idCollaborateur },
            cancellationToken: cancellationToken));

        return rows.AsList();
    }

    public async Task<IReadOnlyList<CommissionItem>> GetCommissionsAsync(Guid idCollaborateur, CancellationToken cancellationToken)
    {
        using var connection = _connectionFactory.CreateConnection();
        await connection.OpenAsync(cancellationToken);

        var rows = await connection.QueryAsync<CommissionItem>(new CommandDefinition(
            @"SELECT po.id_rdv AS IdRdv, r.date_heure_debut AS DateHeureDebut, po.montant AS Montant,
                     po.statut_paiement AS StatutPaiement, u.nom AS NomClient,
                     p.libelle_prestation AS LibellePrestation, p.tarif AS TarifPrestation, 0::numeric AS Commission
              FROM pourboire po
              JOIN rdv r ON r.id_rdv = po.id_rdv
              JOIN prestation p ON p.id_prestation = r.id_prestation
              JOIN utilisateur u ON u.id_utilisateur = r.id_utilisateur_client
              WHERE po.id_collaborateur = @idCollaborateur
              ORDER BY po.date_creation DESC;",
            new { idCollaborateur },
            cancellationToken: cancellationToken));

        return rows.AsList();
    }

    public async Task<string?> GetFicheTechniqueAsync(Guid idRdv, Guid idCollaborateur, CancellationToken cancellationToken)
    {
        using var connection = _connectionFactory.CreateConnection();
        await connection.OpenAsync(cancellationToken);

        return await connection.ExecuteScalarAsync<string?>(new CommandDefinition(
            @"SELECT ft.notes FROM fiche_technique ft
              JOIN rdv r ON r.id_rdv = ft.id_rdv
              WHERE ft.id_rdv = @idRdv AND r.id_collaborateur = @idCollaborateur;",
            new { idRdv, idCollaborateur },
            cancellationToken: cancellationToken));
    }

    public async Task<bool> EnregistrerFicheTechniqueAsync(Guid idRdv, Guid idCollaborateur, string notes, CancellationToken cancellationToken)
    {
        using var connection = _connectionFactory.CreateConnection();
        await connection.OpenAsync(cancellationToken);

        var assigned = await connection.ExecuteScalarAsync<int>(new CommandDefinition(
            "SELECT COUNT(*) FROM rdv WHERE id_rdv = @idRdv AND id_collaborateur = @idCollaborateur;",
            new { idRdv, idCollaborateur },
            cancellationToken: cancellationToken));

        if (assigned == 0)
        {
            return false;
        }

        await connection.ExecuteAsync(new CommandDefinition(
            @"INSERT INTO fiche_technique (id_rdv, id_collaborateur, notes)
              VALUES (@idRdv, @idCollaborateur, @notes)
              ON CONFLICT (id_rdv) DO UPDATE SET notes = @notes, date_modification = now();",
            new { idRdv, idCollaborateur, notes },
            cancellationToken: cancellationToken));

        return true;
    }

    public async Task<string> UpdateWorkSessionAsync(Guid idRdv, Guid idCollaborateur, string action, CancellationToken cancellationToken)
    {
        using var connection=_connectionFactory.CreateConnection(); await connection.OpenAsync(cancellationToken);
        var sql=action switch
        {
            "start" => @"UPDATE rdv SET statut_rdv='EN_COURS',date_debut_reelle=COALESCE(date_debut_reelle,now()),date_pause=NULL WHERE id_rdv=@idRdv AND id_collaborateur=@idCollaborateur AND statut_rdv='CONFIRME' RETURNING 'started';",
            "pause" => @"UPDATE rdv SET date_pause=now() WHERE id_rdv=@idRdv AND id_collaborateur=@idCollaborateur AND statut_rdv='EN_COURS' AND date_pause IS NULL RETURNING 'paused';",
            "resume" => @"UPDATE rdv SET duree_pause_secondes=duree_pause_secondes+EXTRACT(EPOCH FROM(now()-date_pause))::int,date_pause=NULL WHERE id_rdv=@idRdv AND id_collaborateur=@idCollaborateur AND statut_rdv='EN_COURS' AND date_pause IS NOT NULL RETURNING 'resumed';",
            "finish" => @"UPDATE rdv SET statut_rdv='TERMINE',date_fin_reelle=now(),duree_pause_secondes=duree_pause_secondes+CASE WHEN date_pause IS NULL THEN 0 ELSE EXTRACT(EPOCH FROM(now()-date_pause))::int END,date_pause=NULL WHERE id_rdv=@idRdv AND id_collaborateur=@idCollaborateur AND statut_rdv='EN_COURS' RETURNING 'finished';",
            _ => null
        };
        if(sql is null)return "action_invalide";
        return await connection.ExecuteScalarAsync<string?>(new CommandDefinition(sql,new{idRdv,idCollaborateur},cancellationToken:cancellationToken))??"transition_invalide";
    }}
