using Dapper;
using KekeBeauty.Application.Rdv;

namespace KekeBeauty.Infrastructure.Rdv;

public sealed class PourboireRepository : IPourboireRepository
{
    private readonly IDbConnectionFactory _connectionFactory;

    public PourboireRepository(IDbConnectionFactory connectionFactory)
    {
        _connectionFactory = connectionFactory;
    }

    public async Task<bool> CollaboratriceValidePourRdvAsync(Guid idRdv, Guid idCollaborateur, CancellationToken cancellationToken)
    {
        using var connection = _connectionFactory.CreateConnection();
        await connection.OpenAsync(cancellationToken);

        var count = await connection.ExecuteScalarAsync<int>(new CommandDefinition(
            @"SELECT COUNT(*) FROM rdv r
              JOIN collaborateur c ON c.id_etablissement = r.id_etablissement
              WHERE r.id_rdv = @idRdv AND c.id_collaborateur = @idCollaborateur;",
            new { idRdv, idCollaborateur },
            cancellationToken: cancellationToken));

        return count > 0;
    }

    public async Task<Guid> CreerAsync(Guid idRdv, Guid idCollaborateur, decimal montant, string referenceExterne, CancellationToken cancellationToken)
    {
        using var connection = _connectionFactory.CreateConnection();
        await connection.OpenAsync(cancellationToken);

        return await connection.ExecuteScalarAsync<Guid>(new CommandDefinition(
            @"INSERT INTO pourboire (id_rdv, id_collaborateur, montant, reference_externe)
              VALUES (@idRdv, @idCollaborateur, @montant, @referenceExterne)
              RETURNING id_pourboire;",
            new { idRdv, idCollaborateur, montant, referenceExterne, idempotencyKey = $"tip-{idRdv:N}-{idCollaborateur:N}" },
            cancellationToken: cancellationToken));
    }

    public async Task MarquerStatutAsync(string referenceExterne, string statutPaiement, CancellationToken cancellationToken)
    {
        using var connection = _connectionFactory.CreateConnection();
        await connection.OpenAsync(cancellationToken);

        await connection.ExecuteAsync(new CommandDefinition(
            "UPDATE pourboire SET statut_paiement = @statutPaiement WHERE reference_externe = @referenceExterne;",
            new { statutPaiement, referenceExterne },
            cancellationToken: cancellationToken));
    }

    public async Task<PourboireReversementDto?> ObtenirParReferenceAsync(string referenceExterne, CancellationToken cancellationToken)
    {
        using var connection = _connectionFactory.CreateConnection();
        await connection.OpenAsync(cancellationToken);
        return await connection.QuerySingleOrDefaultAsync<PourboireReversementDto>(new CommandDefinition(
            @"SELECT p.id_pourboire AS IdPourboire, p.montant AS Montant, c.telephone AS Telephone,
                     c.nom AS Nom, p.reference_externe AS ReferenceExterne, p.idempotency_key AS IdempotencyKey
              FROM pourboire p JOIN collaborateur c ON c.id_collaborateur = p.id_collaborateur
              WHERE p.reference_externe = @referenceExterne;", new { referenceExterne }, cancellationToken: cancellationToken));
    }
    public async Task<IReadOnlyList<PourboireReversementDto>> ListerAReverserAsync(CancellationToken cancellationToken)
    {
        using var connection = _connectionFactory.CreateConnection();
        await connection.OpenAsync(cancellationToken);
        var rows = await connection.QueryAsync<PourboireReversementDto>(new CommandDefinition(
            @"SELECT p.id_pourboire AS IdPourboire, p.montant AS Montant, c.telephone AS Telephone,
                     c.nom AS Nom, p.reference_externe AS ReferenceExterne, p.idempotency_key AS IdempotencyKey
              FROM pourboire p JOIN collaborateur c ON c.id_collaborateur = p.id_collaborateur
              WHERE p.statut_paiement = 'REUSSIE' AND p.statut_reversement IN ('NON_DEMARRE', 'ECHEC', 'PROCESSING')
                AND p.tentatives_reversement < 5 AND c.telephone IS NOT NULL
              ORDER BY p.date_creation LIMIT 20;", cancellationToken: cancellationToken));
        return rows.AsList();
    }

    public async Task EnregistrerReversementAsync(Guid idPourboire, bool success, string status, string? reference, string? erreur, CancellationToken cancellationToken)
    {
        using var connection = _connectionFactory.CreateConnection();
        await connection.OpenAsync(cancellationToken);
        await connection.ExecuteAsync(new CommandDefinition(
            @"UPDATE pourboire SET tentatives_reversement = tentatives_reversement + 1,
                  date_tentative_reversement = now(), statut_reversement = @status,
                  erreur_reversement = @erreur, reference_reversement = COALESCE(@reference, reference_reversement),
                  date_reversement = CASE WHEN @success THEN now() ELSE date_reversement END
              WHERE id_pourboire = @idPourboire;",
            new { idPourboire, success, status, reference, erreur }, cancellationToken: cancellationToken));
    }}
