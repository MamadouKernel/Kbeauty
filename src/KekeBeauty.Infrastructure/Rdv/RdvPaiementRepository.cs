using Dapper;
using KekeBeauty.Application.Rdv;

namespace KekeBeauty.Infrastructure.Rdv;

public sealed class RdvPaiementRepository : IRdvPaiementRepository
{
    private readonly IDbConnectionFactory _connectionFactory;

    public RdvPaiementRepository(IDbConnectionFactory connectionFactory)
    {
        _connectionFactory = connectionFactory;
    }

    public async Task<RdvPaiementResume> CreerOuReutiliserAsync(Guid idRdv, decimal montant, string referenceExterne, CancellationToken cancellationToken)
    {
        using var connection = _connectionFactory.CreateConnection();
        await connection.OpenAsync(cancellationToken);

        // Idempotent (FR-009) : ON CONFLICT (id_rdv) ne fait rien si une transaction existe deja
        // pour ce RDV - la relecture ci-dessous renvoie alors la ligne existante, jamais une seconde.
        await connection.ExecuteAsync(new CommandDefinition(
            @"INSERT INTO transaction_rdv (id_rdv, montant, statut_transaction, reference_externe)
              VALUES (@idRdv, @montant, 'EN_COURS'::statut_transaction_enum, @referenceExterne)
              ON CONFLICT (id_rdv) DO NOTHING;",
            new { idRdv, montant, referenceExterne },
            cancellationToken: cancellationToken));

        return await ObtenirParRdvAsync(idRdv, cancellationToken)
            ?? throw new InvalidOperationException($"transaction_rdv introuvable juste apres insertion pour le RDV {idRdv}.");
    }

    public async Task<RdvPaiementResume?> ObtenirParRdvAsync(Guid idRdv, CancellationToken cancellationToken)
    {
        using var connection = _connectionFactory.CreateConnection();
        await connection.OpenAsync(cancellationToken);

        return await connection.QuerySingleOrDefaultAsync<RdvPaiementResume>(new CommandDefinition(
            @"SELECT id_transaction_rdv AS IdTransactionRdv, id_rdv AS IdRdv, montant AS Montant,
                     statut_transaction AS StatutTransaction, reference_externe AS ReferenceExterne
              FROM transaction_rdv WHERE id_rdv = @idRdv;",
            new { idRdv },
            cancellationToken: cancellationToken));
    }

    public async Task<bool> MarquerPaiementAsync(string referenceExterne, bool paiementReussi, string? operateurExterne, CancellationToken cancellationToken)
    {
        using var connection = _connectionFactory.CreateConnection();
        await connection.OpenAsync(cancellationToken);

        var statutTransaction = paiementReussi ? "REUSSIE" : "ECHOUEE";

        // Idempotent : seule une transaction encore EN_COURS est mise a jour (meme pattern que
        // AbonnementRepository.MarquerPaiementAsync). Ne touche jamais rdv.statut_rdv (FR-010).
        var rows = await connection.ExecuteAsync(new CommandDefinition(
            @"UPDATE transaction_rdv
              SET statut_transaction = @statutTransaction::statut_transaction_enum,
                  operateur_externe = @operateurExterne, date_maj = now()
              WHERE reference_externe = @referenceExterne AND statut_transaction = 'EN_COURS';",
            new { referenceExterne, statutTransaction, operateurExterne },
            cancellationToken: cancellationToken));

        return rows > 0;
    }

    public async Task<bool> RelancerAsync(Guid idRdv, string nouvelleReferenceExterne, CancellationToken cancellationToken)
    {
        using var connection = _connectionFactory.CreateConnection();
        await connection.OpenAsync(cancellationToken);

        var rows = await connection.ExecuteAsync(new CommandDefinition(
            @"UPDATE transaction_rdv
              SET statut_transaction = 'EN_COURS'::statut_transaction_enum,
                  reference_externe = @nouvelleReferenceExterne, operateur_externe = NULL, date_maj = now()
              WHERE id_rdv = @idRdv AND statut_transaction = 'ECHOUEE';",
            new { idRdv, nouvelleReferenceExterne },
            cancellationToken: cancellationToken));

        return rows > 0;
    }

    public async Task<bool> DemanderRembourseAsync(Guid idRdv, CancellationToken cancellationToken)
    {
        using var connection = _connectionFactory.CreateConnection();
        await connection.OpenAsync(cancellationToken);

        var rows = await connection.ExecuteAsync(new CommandDefinition(
            @"UPDATE transaction_rdv SET statut_transaction = 'REMBOURSEMENT_DEMANDE'::statut_transaction_enum, eligible_keke_protect = true, date_maj = now()
              WHERE id_rdv = @idRdv AND statut_transaction = 'REUSSIE';",
            new { idRdv },
            cancellationToken: cancellationToken));

        return rows > 0;
    }

    public async Task<IReadOnlyList<DemandeRemboursementDto>> ListerDemandesRemboursementAsync(CancellationToken cancellationToken)
    {
        using var connection = _connectionFactory.CreateConnection();
        await connection.OpenAsync(cancellationToken);

        var rows = await connection.QueryAsync<DemandeRemboursementDto>(new CommandDefinition(
            @"SELECT tr.id_rdv AS IdRdv, tr.montant AS Montant, tr.date_maj AS DateTransaction,
                     e.nom_etablissement AS NomEtablissement, u.telephone AS TelephoneClient
              FROM transaction_rdv tr
              JOIN rdv r ON r.id_rdv = tr.id_rdv
              JOIN etablissement e ON e.id_etablissement = r.id_etablissement
              JOIN utilisateur u ON u.id_utilisateur = r.id_utilisateur_client
              WHERE tr.statut_transaction = 'REMBOURSEMENT_DEMANDE'
              ORDER BY tr.date_maj;",
            cancellationToken: cancellationToken));

        return rows.AsList();
    }

    public async Task<bool> TraiterRemboursementAsync(Guid idRdv, string referenceRemboursement, CancellationToken cancellationToken)
    {
        using var connection = _connectionFactory.CreateConnection();
        await connection.OpenAsync(cancellationToken);

        var rows = await connection.ExecuteAsync(new CommandDefinition(
            @"UPDATE transaction_rdv
              SET statut_transaction = 'REMBOURSEE'::statut_transaction_enum, date_maj = now(),
                  reference_remboursement = @referenceRemboursement, date_remboursement = now()
              WHERE id_rdv = @idRdv AND statut_transaction = 'REMBOURSEMENT_DEMANDE';",
            new { idRdv, referenceRemboursement },
            cancellationToken: cancellationToken));

        return rows > 0;
    }

    public async Task<RemboursementWaveDto?> ObtenirRemboursementWaveAsync(Guid idRdv, CancellationToken cancellationToken)
    {
        using var connection = _connectionFactory.CreateConnection();
        await connection.OpenAsync(cancellationToken);
        return await connection.QuerySingleOrDefaultAsync<RemboursementWaveDto>(new CommandDefinition(
            @"SELECT id_rdv AS IdRdv, montant AS Montant, reference_externe AS ReferenceExterne
              FROM transaction_rdv WHERE id_rdv = @idRdv AND statut_transaction = 'REMBOURSEMENT_DEMANDE';",
            new { idRdv }, cancellationToken: cancellationToken));
    }

    public async Task EnregistrerTentativeRemboursementAsync(Guid idRdv, bool success, string? reference, string? erreur, CancellationToken cancellationToken)
    {
        using var connection = _connectionFactory.CreateConnection();
        await connection.OpenAsync(cancellationToken);
        await connection.ExecuteAsync(new CommandDefinition(
            @"UPDATE transaction_rdv SET tentatives_remboursement = tentatives_remboursement + 1,
                  date_tentative_remboursement = now(), statut_remboursement = @statut, erreur_remboursement = @erreur,
                  reference_remboursement = CASE WHEN @success THEN @reference ELSE reference_remboursement END,
                  date_remboursement = CASE WHEN @success THEN now() ELSE date_remboursement END,
                  statut_transaction = CASE WHEN @success THEN 'REMBOURSEE'::statut_transaction_enum ELSE statut_transaction END,
                  date_maj = now()
              WHERE id_rdv = @idRdv AND statut_transaction = 'REMBOURSEMENT_DEMANDE';",
            new { idRdv, success, reference, erreur, statut = success ? "REUSSI" : "ECHEC" }, cancellationToken: cancellationToken));
    }}
