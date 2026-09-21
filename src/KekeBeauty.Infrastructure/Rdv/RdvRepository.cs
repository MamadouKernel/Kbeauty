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

        var result = rows.AsList();
        var closures = await connection.QueryAsync<CreneauOccupe>(new CommandDefinition(
            @"SELECT date_debut AS DateHeureDebut, date_fin AS DateHeureFin FROM indisponibilite_etablissement
              WHERE id_etablissement=@idEtablissement AND date_debut < @jour + interval '1 day' AND date_fin > @jour;",
            new { idEtablissement, jour }, cancellationToken: cancellationToken));
        result.AddRange(closures);
        return result;
    }

    public async Task<Guid?> CreateIfNoOverlapAsync(
        Guid idEtablissement, Guid idPrestation, Guid idUtilisateurClient, DateTimeOffset dateHeureDebut, string? modePaiementChoisi, CancellationToken cancellationToken)
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
            @"INSERT INTO rdv (id_etablissement, id_prestation, id_utilisateur_client, date_heure_debut, statut_rdv, mode_paiement_service)
              SELECT @idEtablissement, @idPrestation, @idUtilisateurClient, @dateHeureDebut, 'DEMANDE',
                     CASE
                       WHEN NOT pf.paiement_mobile THEN 'ESPECES'
                       WHEN e.mode_paiement_service = 'MIXTE' THEN @modePaiementChoisi
                       ELSE e.mode_paiement_service
                     END
              FROM etablissement e
              JOIN parametre_formule pf ON pf.formule = CASE
                  WHEN EXISTS (SELECT 1 FROM abonnement a WHERE a.id_etablissement = e.id_etablissement AND a.statut_abonnement = 'ACTIF') THEN 'PRO'
                  ELSE 'FREE' END
              WHERE e.id_etablissement = @idEtablissement
                AND (NOT pf.paiement_mobile
                     OR e.mode_paiement_service <> 'MIXTE' OR @modePaiementChoisi IN ('ESPECES', 'EN_LIGNE'))
                AND NOT EXISTS (
                SELECT 1 FROM indisponibilite_etablissement i JOIN prestation np ON np.id_prestation=@idPrestation
                WHERE i.id_etablissement=@idEtablissement AND @dateHeureDebut < i.date_fin
                  AND i.date_debut < @dateHeureDebut + (np.duree_minutes * interval '1 minute'))
                AND NOT EXISTS (
                SELECT 1 FROM rdv r
                JOIN prestation p ON p.id_prestation = r.id_prestation
                JOIN prestation np ON np.id_prestation = @idPrestation
                WHERE r.id_etablissement = @idEtablissement
                  AND r.statut_rdv IN ('DEMANDE', 'CONFIRME')
                  AND @dateHeureDebut < r.date_heure_debut + (p.duree_minutes * interval '1 minute')
                  AND r.date_heure_debut < @dateHeureDebut + (np.duree_minutes * interval '1 minute')
              )
              RETURNING id_rdv;",
            new { idEtablissement, idPrestation, idUtilisateurClient, dateHeureDebut, modePaiementChoisi = modePaiementChoisi?.Trim().ToUpperInvariant() },
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

    public async Task<string?> GetClientNomAsync(Guid idRdv, CancellationToken cancellationToken)
    {
        using var connection = _connectionFactory.CreateConnection();
        await connection.OpenAsync(cancellationToken);

        return await connection.ExecuteScalarAsync<string?>(new CommandDefinition(
            @"SELECT u.nom FROM rdv r JOIN utilisateur u ON u.id_utilisateur = r.id_utilisateur_client
              WHERE r.id_rdv = @idRdv;",
            new { idRdv },
            cancellationToken: cancellationToken));
    }

    public async Task<Guid?> GetClientIdAsync(Guid idRdv, CancellationToken cancellationToken)
    {
        using var connection = _connectionFactory.CreateConnection();
        await connection.OpenAsync(cancellationToken);

        return await connection.ExecuteScalarAsync<Guid?>(new CommandDefinition(
            "SELECT id_utilisateur_client FROM rdv WHERE id_rdv = @idRdv;",
            new { idRdv },
            cancellationToken: cancellationToken));
    }

    public async Task<bool> SignalerRetardAsync(Guid idRdv, Guid idUtilisateurClient, short minutes, CancellationToken cancellationToken)
    {
        using var connection=_connectionFactory.CreateConnection();await connection.OpenAsync(cancellationToken);
        return await connection.ExecuteAsync(new CommandDefinition(@"UPDATE rdv SET retard_minutes=@minutes,date_signalement_retard=now()
            WHERE id_rdv=@idRdv AND id_utilisateur_client=@idUtilisateurClient AND statut_rdv='CONFIRME' AND date_heure_debut > now()-interval '1 hour';",
            new{idRdv,idUtilisateurClient,minutes},cancellationToken:cancellationToken))>0;
    }
    private sealed class DateHeureDebutRow
    {
        public DateTimeOffset DateHeureDebut { get; set; }
    }

    public async Task<DateTimeOffset?> GetDateHeureDebutAsync(Guid idRdv, CancellationToken cancellationToken)
    {
        using var connection = _connectionFactory.CreateConnection();
        await connection.OpenAsync(cancellationToken);

        // Note : ExecuteScalarAsync<DateTimeOffset?> echoue (InvalidCastException DateTime ->
        // DateTimeOffset) car Npgsql renvoie un DateTime brut pour timestamptz et Dapper ne
        // convertit ce cast que lors du mapping vers une propriete typee d'une classe (meme
        // convention deja etablie ailleurs dans le projet : classes mutables plutot que scalaires/
        // tuples pour la materialisation Dapper).
        var row = await connection.QuerySingleOrDefaultAsync<DateHeureDebutRow>(new CommandDefinition(
            "SELECT date_heure_debut AS DateHeureDebut FROM rdv WHERE id_rdv = @idRdv;",
            new { idRdv },
            cancellationToken: cancellationToken));

        return row?.DateHeureDebut;
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

    public async Task<RdvStatutRow?> GetStatutAsync(Guid idRdv, Guid idUtilisateurClient, CancellationToken cancellationToken)
    {
        using var connection = _connectionFactory.CreateConnection();
        await connection.OpenAsync(cancellationToken);

        return await connection.QuerySingleOrDefaultAsync<RdvStatutRow>(new CommandDefinition(
            @"SELECT id_rdv AS IdRdv, statut_rdv AS StatutRdv, date_heure_debut AS DateHeureDebut
              FROM rdv WHERE id_rdv = @idRdv AND id_utilisateur_client = @idUtilisateurClient;",
            new { idRdv, idUtilisateurClient },
            cancellationToken: cancellationToken));
    }

    public async Task<IReadOnlyList<RdvPartenaireRow>> ListByEtablissementAsync(Guid idEtablissement, CancellationToken cancellationToken)
    {
        using var connection = _connectionFactory.CreateConnection();
        await connection.OpenAsync(cancellationToken);

        var rows = await connection.QueryAsync<RdvPartenaireRow>(new CommandDefinition(
            @"SELECT r.id_rdv AS IdRdv, r.statut_rdv AS StatutRdv, r.date_heure_debut AS DateHeureDebut,
                     p.libelle_prestation AS LibellePrestation, u.telephone AS TelephoneClient, u.nom AS NomClient,
                     p.duree_minutes AS DureeMinutes, p.tarif AS TarifPrestation, r.origine_rdv AS OrigineRdv,
                     r.id_collaborateur AS IdCollaborateur, r.retard_minutes AS RetardMinutes, r.date_signalement_retard AS DateSignalementRetard
              FROM rdv r
              JOIN prestation p ON p.id_prestation = r.id_prestation
              JOIN utilisateur u ON u.id_utilisateur = r.id_utilisateur_client
              WHERE r.id_etablissement = @idEtablissement
              ORDER BY r.date_heure_debut DESC;",
            new { idEtablissement },
            cancellationToken: cancellationToken));

        return rows.AsList();
    }

    public async Task<(Guid? IdRdv, string Status)> CreateWalkInAsync(Guid idEtablissement, Guid idPrestation,
        Guid? idCollaborateur, string nomClient, string telephoneClient, DateTimeOffset dateHeureDebut,
        string modePaiement, CancellationToken cancellationToken)
    {
        using var connection = _connectionFactory.CreateConnection(); await connection.OpenAsync(cancellationToken);
        if (idCollaborateur is not null && await connection.ExecuteScalarAsync<int>(new CommandDefinition(
            "SELECT count(*) FROM collaborateur WHERE id_collaborateur=@idCollaborateur AND id_etablissement=@idEtablissement;",
            new{idCollaborateur,idEtablissement},cancellationToken:cancellationToken)) == 0) return (null,"collaboratrice_invalide");
        var clientId = await connection.ExecuteScalarAsync<Guid>(new CommandDefinition(@"INSERT INTO utilisateur(telephone,nom,type_compte,est_actif)
            VALUES(@telephoneClient,@nomClient,'CLIENT',true) ON CONFLICT(telephone) DO UPDATE SET nom=CASE WHEN utilisateur.nom='' THEN EXCLUDED.nom ELSE utilisateur.nom END
            RETURNING id_utilisateur;",new{telephoneClient,nomClient},cancellationToken:cancellationToken));
        var idRdv = await CreateIfNoOverlapAsync(idEtablissement,idPrestation,clientId,dateHeureDebut,modePaiement,cancellationToken);
        if(idRdv is null) return(null,"boutique_ou_prestation_invalide"); if(idRdv==Guid.Empty)return(null,"creneau_indisponible");
        await connection.ExecuteAsync(new CommandDefinition("UPDATE rdv SET statut_rdv='CONFIRME',id_collaborateur=@idCollaborateur,origine_rdv='COMPTOIR' WHERE id_rdv=@idRdv;",new{idRdv,idCollaborateur},cancellationToken:cancellationToken));
        return(idRdv,"created");
    }
    public async Task<IReadOnlyList<RdvHistoriqueRow>> ListByClientAsync(Guid idUtilisateurClient, CancellationToken cancellationToken)
    {
        using var connection = _connectionFactory.CreateConnection();
        await connection.OpenAsync(cancellationToken);

        var rows = await connection.QueryAsync<RdvHistoriqueRow>(new CommandDefinition(
            @"SELECT r.id_rdv AS IdRdv, r.id_etablissement AS IdEtablissement, e.nom_etablissement AS NomEtablissement, p.libelle_prestation AS LibellePrestation,
                     r.date_heure_debut AS DateHeureDebut, r.statut_rdv AS StatutRdv, tr.statut_transaction AS StatutPaiement,
                     r.mode_paiement_service AS ModePaiementService, (a.id_avis IS NOT NULL) AS ADejaAvis, r.id_collaborateur AS IdCollaborateur, r.retard_minutes AS RetardMinutes, r.date_signalement_retard AS DateSignalementRetard, p.tarif AS TarifPrestation
              FROM rdv r
              JOIN etablissement e ON e.id_etablissement = r.id_etablissement
              JOIN prestation p ON p.id_prestation = r.id_prestation
              LEFT JOIN transaction_rdv tr ON tr.id_rdv = r.id_rdv
              LEFT JOIN avis a ON a.id_rdv = r.id_rdv
              WHERE r.id_utilisateur_client = @idUtilisateurClient
              ORDER BY r.date_heure_debut DESC;",
            new { idUtilisateurClient },
            cancellationToken: cancellationToken));

        return rows.AsList();
    }

    public async Task<bool> AnnulerParClientAsync(Guid idRdv, Guid idUtilisateurClient, CancellationToken cancellationToken)
    {
        using var connection = _connectionFactory.CreateConnection();
        await connection.OpenAsync(cancellationToken);

        var rows = await connection.ExecuteAsync(new CommandDefinition(
            @"UPDATE rdv SET statut_rdv = 'ANNULE'::statut_rdv_enum
              WHERE id_rdv = @idRdv AND id_utilisateur_client = @idUtilisateurClient
                AND statut_rdv IN ('DEMANDE', 'CONFIRME');",
            new { idRdv, idUtilisateurClient },
            cancellationToken: cancellationToken));

        return rows > 0;
    }

    public async Task<decimal?> GetTarifPrestationAsync(Guid idPrestation, CancellationToken cancellationToken)
    {
        using var connection = _connectionFactory.CreateConnection();
        await connection.OpenAsync(cancellationToken);

        return await connection.ExecuteScalarAsync<decimal?>(new CommandDefinition(
            "SELECT tarif FROM prestation WHERE id_prestation = @idPrestation;",
            new { idPrestation },
            cancellationToken: cancellationToken));
    }

    public async Task<string?> GetModePaiementServiceRdvAsync(Guid idRdv, CancellationToken cancellationToken)
    {
        using var connection = _connectionFactory.CreateConnection();
        await connection.OpenAsync(cancellationToken);
        return await connection.ExecuteScalarAsync<string?>(new CommandDefinition(
            "SELECT mode_paiement_service FROM rdv WHERE id_rdv = @idRdv;",
            new { idRdv }, cancellationToken: cancellationToken));
    }

    public async Task<bool> AssignerCollaborateurAsync(Guid idEtablissement, Guid idRdv, Guid? idCollaborateur, CancellationToken cancellationToken)
    {
        using var connection = _connectionFactory.CreateConnection();
        await connection.OpenAsync(cancellationToken);

        var rows = await connection.ExecuteAsync(new CommandDefinition(
            @"UPDATE rdv SET id_collaborateur = @idCollaborateur
              WHERE id_rdv = @idRdv AND id_etablissement = @idEtablissement;",
            new { idRdv, idEtablissement, idCollaborateur },
            cancellationToken: cancellationToken));

        return rows > 0;
    }

    public async Task<IReadOnlyList<RdvTermineInfo>> MarquerRdvsExpiresCommeTerminesAsync(CancellationToken cancellationToken)
    {
        using var connection = _connectionFactory.CreateConnection();
        await connection.OpenAsync(cancellationToken);

        var rows = await connection.QueryAsync<RdvTermineInfo>(new CommandDefinition(
            @"UPDATE rdv r SET statut_rdv = 'TERMINE'
              FROM prestation p, etablissement e
              WHERE r.id_prestation = p.id_prestation
                AND r.id_etablissement = e.id_etablissement
                AND r.statut_rdv = 'CONFIRME'
                AND r.date_heure_debut + (p.duree_minutes * interval '1 minute') <= now()
              RETURNING r.id_rdv AS IdRdv, r.id_utilisateur_client AS IdUtilisateurClient,
                        r.id_collaborateur AS IdCollaborateur, e.nom_etablissement AS NomEtablissement,
                        r.date_heure_debut AS DateHeureDebut;",
            cancellationToken: cancellationToken));

        return rows.AsList();
    }
    public async Task<IReadOnlyList<RdvReminderInfo>> ListerRappelsAsync(CancellationToken cancellationToken)
    {
        using var connection = _connectionFactory.CreateConnection(); await connection.OpenAsync(cancellationToken);
        var rows = await connection.QueryAsync<RdvReminderInfo>(new CommandDefinition(
            @"SELECT r.id_rdv AS IdRdv, r.id_utilisateur_client AS IdUtilisateurClient, e.nom_etablissement AS NomEtablissement, r.date_heure_debut AS DateHeureDebut
              FROM rdv r JOIN etablissement e ON e.id_etablissement=r.id_etablissement JOIN utilisateur u ON u.id_utilisateur=r.id_utilisateur_client
              WHERE r.statut_rdv='CONFIRME' AND u.notifications_rdv=TRUE
                AND r.date_heure_debut BETWEEN now()+interval '23 hours' AND now()+interval '24 hours'
                AND NOT EXISTS(SELECT 1 FROM notification n WHERE n.id_rdv=r.id_rdv AND n.titre='Rappel de rendez-vous');",
            cancellationToken:cancellationToken));
        return rows.AsList();
    }
}

