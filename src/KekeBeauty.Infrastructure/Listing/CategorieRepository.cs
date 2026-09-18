using Dapper;
using KekeBeauty.Application.Directory;

namespace KekeBeauty.Infrastructure.Listing;

public sealed class CategorieRepository : ICategorieRepository
{
    private readonly IDbConnectionFactory _connectionFactory;

    public CategorieRepository(IDbConnectionFactory connectionFactory)
    {
        _connectionFactory = connectionFactory;
    }

    public async Task<Guid> FindOrCreateByLibelleAsync(string libelleCategorie, CancellationToken cancellationToken)
    {
        using var connection = _connectionFactory.CreateConnection();
        await connection.OpenAsync(cancellationToken);

        // Find-or-create idempotent (voir research.md, Decision 3) : pas de migration de seed.
        await connection.ExecuteAsync(new CommandDefinition(
            "INSERT INTO categorie (libelle_categorie) VALUES (@libelleCategorie) ON CONFLICT (libelle_categorie) DO NOTHING;",
            new { libelleCategorie },
            cancellationToken: cancellationToken));

        return await connection.ExecuteScalarAsync<Guid>(new CommandDefinition(
            "SELECT id_categorie FROM categorie WHERE libelle_categorie = @libelleCategorie;",
            new { libelleCategorie },
            cancellationToken: cancellationToken));
    }

    public async Task AssignToEtablissementAsync(Guid idEtablissement, Guid idCategorie, CancellationToken cancellationToken)
    {
        using var connection = _connectionFactory.CreateConnection();
        await connection.OpenAsync(cancellationToken);

        await connection.ExecuteAsync(new CommandDefinition(
            @"INSERT INTO etablissement_categorie (id_etablissement, id_categorie)
              VALUES (@idEtablissement, @idCategorie) ON CONFLICT DO NOTHING;",
            new { idEtablissement, idCategorie },
            cancellationToken: cancellationToken));
    }

    public async Task<bool> EtablissementExistsAsync(Guid idEtablissement, CancellationToken cancellationToken)
    {
        using var connection = _connectionFactory.CreateConnection();
        await connection.OpenAsync(cancellationToken);

        var count = await connection.ExecuteScalarAsync<int>(new CommandDefinition(
            "SELECT count(*) FROM etablissement WHERE id_etablissement = @idEtablissement;",
            new { idEtablissement },
            cancellationToken: cancellationToken));

        return count > 0;
    }
}
