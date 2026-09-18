using Dapper;
using KekeBeauty.Application.Auth;
using KekeBeauty.Domain.Entities;

namespace KekeBeauty.Infrastructure.Auth;

public sealed class UtilisateurRepository : IUtilisateurRepository
{
    private readonly IDbConnectionFactory _connectionFactory;

    public UtilisateurRepository(IDbConnectionFactory connectionFactory)
    {
        _connectionFactory = connectionFactory;
    }

    public async Task<Utilisateur?> FindByTelephoneAsync(string telephone, TypeCompte typeCompte, CancellationToken cancellationToken)
    {
        using var connection = _connectionFactory.CreateConnection();
        await connection.OpenAsync(cancellationToken);

        return await connection.QuerySingleOrDefaultAsync<Utilisateur>(new CommandDefinition(
            @"SELECT id_utilisateur AS IdUtilisateur, telephone AS Telephone, nom AS Nom,
                     type_compte AS TypeCompte, date_creation AS DateCreation, est_suspendu AS EstSuspendu
              FROM utilisateur
              WHERE telephone = @telephone AND type_compte = @typeCompte::type_compte_enum;",
            new { telephone, typeCompte = typeCompte.ToString().ToUpperInvariant() },
            cancellationToken: cancellationToken));
    }

    public async Task<(Utilisateur Utilisateur, bool IsNewAccount)> FindOrCreateAsync(
        string telephone, TypeCompte typeCompte, CancellationToken cancellationToken)
    {
        var existing = await FindByTelephoneAsync(telephone, typeCompte, cancellationToken);
        if (existing is not null)
        {
            return (existing, false);
        }

        using var connection = _connectionFactory.CreateConnection();
        await connection.OpenAsync(cancellationToken);

        // FR-007 applique par la contrainte UNIQUE (telephone, type_compte) deja en base :
        // en cas de course entre deux requetes concurrentes, ON CONFLICT DO NOTHING evite une
        // erreur applicative, puis on relit la ligne (creee par l'une ou l'autre requete).
        await connection.ExecuteAsync(new CommandDefinition(
            @"INSERT INTO utilisateur (telephone, nom, type_compte)
              VALUES (@telephone, @telephone, @typeCompte::type_compte_enum)
              ON CONFLICT (telephone, type_compte) DO NOTHING;",
            new { telephone, typeCompte = typeCompte.ToString().ToUpperInvariant() },
            cancellationToken: cancellationToken));

        var created = await FindByTelephoneAsync(telephone, typeCompte, cancellationToken)
            ?? throw new InvalidOperationException("Echec inattendu de creation/lecture de l'utilisateur.");

        return (created, true);
    }
}
