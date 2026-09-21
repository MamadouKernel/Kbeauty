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
                     type_compte AS TypeCompte, date_creation AS DateCreation, est_suspendu AS EstSuspendu, email AS Email, notifications_rdv AS NotificationsRdv, notifications_marketing AS NotificationsMarketing, consentement_donnees AS ConsentementDonnees, date_suppression AS DateSuppression
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
    public async Task<Utilisateur?> GetProfileAsync(Guid idUtilisateur, CancellationToken cancellationToken)
    {
        using var connection = _connectionFactory.CreateConnection(); await connection.OpenAsync(cancellationToken);
        return await connection.QuerySingleOrDefaultAsync<Utilisateur>(new CommandDefinition(
            @"SELECT id_utilisateur AS IdUtilisateur, telephone AS Telephone, nom AS Nom, type_compte AS TypeCompte,
                     date_creation AS DateCreation, est_suspendu AS EstSuspendu, email AS Email,
                     notifications_rdv AS NotificationsRdv, notifications_marketing AS NotificationsMarketing,
                     consentement_donnees AS ConsentementDonnees, date_suppression AS DateSuppression
              FROM utilisateur WHERE id_utilisateur=@idUtilisateur AND date_suppression IS NULL;",
            new { idUtilisateur }, cancellationToken: cancellationToken));
    }

    public async Task<bool> UpdateProfileAsync(Guid idUtilisateur, string nom, string? email, bool notificationsRdv, bool notificationsMarketing, bool consentementDonnees, CancellationToken cancellationToken)
    {
        using var connection = _connectionFactory.CreateConnection(); await connection.OpenAsync(cancellationToken);
        var rows = await connection.ExecuteAsync(new CommandDefinition(
            @"UPDATE utilisateur SET nom=@nom, email=NULLIF(@email,''), notifications_rdv=@notificationsRdv,
                     notifications_marketing=@notificationsMarketing, consentement_donnees=@consentementDonnees
              WHERE id_utilisateur=@idUtilisateur AND date_suppression IS NULL;",
            new { idUtilisateur, nom, email, notificationsRdv, notificationsMarketing, consentementDonnees }, cancellationToken: cancellationToken));
        return rows > 0;
    }

    public async Task<(Utilisateur Utilisateur, bool IsNewAccount)> FindOrCreateGoogleAsync(
        string googleSubject, string? email, string nom, TypeCompte typeCompte, CancellationToken cancellationToken)
    {
        using var connection = _connectionFactory.CreateConnection();
        await connection.OpenAsync(cancellationToken);

        var existing = await connection.QuerySingleOrDefaultAsync<Utilisateur>(new CommandDefinition(
            @"SELECT id_utilisateur AS IdUtilisateur, COALESCE(telephone, '') AS Telephone, nom AS Nom,
                     type_compte AS TypeCompte, date_creation AS DateCreation, est_suspendu AS EstSuspendu,
                     email AS Email, notifications_rdv AS NotificationsRdv,
                     notifications_marketing AS NotificationsMarketing,
                     consentement_donnees AS ConsentementDonnees, date_suppression AS DateSuppression
              FROM utilisateur
              WHERE google_subject=@googleSubject AND type_compte=@typeCompte::type_compte_enum AND date_suppression IS NULL;",
            new { googleSubject, typeCompte = typeCompte.ToString().ToUpperInvariant() }, cancellationToken: cancellationToken));
        if (existing is not null) return (existing, false);

        // Rattache un compte OTP existant lorsque Google confirme la meme adresse email.
        if (!string.IsNullOrWhiteSpace(email))
        {
            var linked = await connection.QuerySingleOrDefaultAsync<Utilisateur>(new CommandDefinition(
                @"UPDATE utilisateur SET google_subject=@googleSubject
                  WHERE id_utilisateur=(SELECT id_utilisateur FROM utilisateur
                    WHERE lower(email)=lower(@email) AND type_compte=@typeCompte::type_compte_enum AND date_suppression IS NULL LIMIT 1)
                    AND google_subject IS NULL
                  RETURNING id_utilisateur AS IdUtilisateur, COALESCE(telephone, '') AS Telephone, nom AS Nom,
                            type_compte AS TypeCompte, date_creation AS DateCreation, est_suspendu AS EstSuspendu,
                            email AS Email, notifications_rdv AS NotificationsRdv,
                            notifications_marketing AS NotificationsMarketing,
                            consentement_donnees AS ConsentementDonnees, date_suppression AS DateSuppression;",
                new { googleSubject, email, typeCompte = typeCompte.ToString().ToUpperInvariant() }, cancellationToken: cancellationToken));
            if (linked is not null) return (linked, false);
        }
        var created = await connection.QuerySingleAsync<Utilisateur>(new CommandDefinition(
            @"INSERT INTO utilisateur (telephone, nom, email, type_compte, google_subject)
              VALUES (NULL, @nom, @email, @typeCompte::type_compte_enum, @googleSubject)
              ON CONFLICT (google_subject, type_compte) WHERE google_subject IS NOT NULL AND date_suppression IS NULL
              DO UPDATE SET email=EXCLUDED.email
              RETURNING id_utilisateur AS IdUtilisateur, COALESCE(telephone, '') AS Telephone, nom AS Nom,
                        type_compte AS TypeCompte, date_creation AS DateCreation, est_suspendu AS EstSuspendu,
                        email AS Email, notifications_rdv AS NotificationsRdv,
                        notifications_marketing AS NotificationsMarketing,
                        consentement_donnees AS ConsentementDonnees, date_suppression AS DateSuppression;",
            new { googleSubject, email, typeCompte = typeCompte.ToString().ToUpperInvariant(), nom = nom.Trim()[..Math.Min(nom.Trim().Length, 100)] },
            cancellationToken: cancellationToken));
        return (created, true);
    }
    public async Task<bool> UpdatePartnerTelephoneAsync(Guid idUtilisateur, string telephone, CancellationToken cancellationToken)
    {
        using var connection = _connectionFactory.CreateConnection();
        await connection.OpenAsync(cancellationToken);
        var rows = await connection.ExecuteAsync(new CommandDefinition(
            @"UPDATE utilisateur SET telephone=@telephone
              WHERE id_utilisateur=@idUtilisateur AND type_compte='PARTENAIRE' AND date_suppression IS NULL;",
            new { idUtilisateur, telephone }, cancellationToken: cancellationToken));
        return rows > 0;
    }
    public async Task StorePartnerOnboardingTokenAsync(Guid idUtilisateur, string tokenHash, DateTimeOffset expireLe, CancellationToken cancellationToken)
    {
        using var connection = _connectionFactory.CreateConnection();
        await connection.OpenAsync(cancellationToken);
        await connection.ExecuteAsync(new CommandDefinition(
            @"DELETE FROM google_partner_onboarding_token WHERE id_utilisateur=@idUtilisateur OR expire_le <= now();
              INSERT INTO google_partner_onboarding_token (token_hash, id_utilisateur, expire_le)
              VALUES (@tokenHash, @idUtilisateur, @expireLe);",
            new { idUtilisateur, tokenHash, expireLe }, cancellationToken: cancellationToken));
    }

    public async Task<Guid?> ConsumePartnerOnboardingTokenAsync(string tokenHash, string telephone, CancellationToken cancellationToken)
    {
        using var connection = _connectionFactory.CreateConnection();
        await connection.OpenAsync(cancellationToken);
        using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        var idUtilisateur = await connection.ExecuteScalarAsync<Guid?>(new CommandDefinition(
            @"SELECT id_utilisateur FROM google_partner_onboarding_token
              WHERE token_hash=@tokenHash AND expire_le > now();", new { tokenHash }, transaction, cancellationToken: cancellationToken));
        if (idUtilisateur is null) { await transaction.RollbackAsync(cancellationToken); return null; }

        // Le numero peut deja appartenir a un compte partenaire cree par WhatsApp/OTP.
        // On rattache alors Google a ce compte existant : ses boutiques sont conservees
        // et le meme gerant peut creer plusieurs etablissements.
        var existingPhoneUserId = await connection.ExecuteScalarAsync<Guid?>(new CommandDefinition(
            @"SELECT id_utilisateur FROM utilisateur
              WHERE telephone=@telephone AND type_compte='PARTENAIRE'
                AND date_suppression IS NULL AND id_utilisateur<>@idUtilisateur
              LIMIT 1;",
            new { telephone, idUtilisateur }, transaction, cancellationToken: cancellationToken));

        if (existingPhoneUserId is not null)
        {
            var googleIdentity = await connection.QuerySingleAsync(new CommandDefinition(
                @"SELECT google_subject AS GoogleSubject, email AS Email, nom AS Nom
                  FROM utilisateur WHERE id_utilisateur=@idUtilisateur;",
                new { idUtilisateur }, transaction, cancellationToken: cancellationToken));

            await connection.ExecuteAsync(new CommandDefinition(
                @"UPDATE utilisateur SET google_subject=NULL WHERE id_utilisateur=@idUtilisateur;
                  UPDATE utilisateur
                     SET google_subject=@GoogleSubject,
                         email=COALESCE(@Email, email),
                         nom=CASE WHEN nom=telephone THEN @Nom ELSE nom END
                   WHERE id_utilisateur=@existingPhoneUserId;
                  UPDATE google_partner_onboarding_token
                     SET id_utilisateur=@existingPhoneUserId
                   WHERE token_hash=@tokenHash;",
                new { idUtilisateur, existingPhoneUserId, tokenHash, googleIdentity.GoogleSubject, googleIdentity.Email, googleIdentity.Nom },
                transaction, cancellationToken: cancellationToken));
            idUtilisateur = existingPhoneUserId;
        }

        var updated = await connection.ExecuteAsync(new CommandDefinition(
            @"UPDATE utilisateur SET telephone=@telephone
              WHERE id_utilisateur=@idUtilisateur AND type_compte='PARTENAIRE' AND date_suppression IS NULL;",
            new { idUtilisateur, telephone }, transaction, cancellationToken: cancellationToken));
        if (updated == 0) { await transaction.RollbackAsync(cancellationToken); return null; }
        await transaction.CommitAsync(cancellationToken);
        return idUtilisateur;
    }    public async Task DeletePartnerOnboardingTokenAsync(Guid idUtilisateur, CancellationToken cancellationToken)
    {
        using var connection = _connectionFactory.CreateConnection();
        await connection.OpenAsync(cancellationToken);
        await connection.ExecuteAsync(new CommandDefinition(
            "DELETE FROM google_partner_onboarding_token WHERE id_utilisateur=@idUtilisateur;",
            new { idUtilisateur }, cancellationToken: cancellationToken));
    }
    public async Task<bool> UpdatePartnerPhotoAsync(Guid idUtilisateur, string relativePath, CancellationToken cancellationToken)
    {
        using var connection = _connectionFactory.CreateConnection(); await connection.OpenAsync(cancellationToken);
        return await connection.ExecuteAsync(new CommandDefinition(
            "UPDATE utilisateur SET url_photo_profil=@relativePath WHERE id_utilisateur=@idUtilisateur AND type_compte='PARTENAIRE' AND date_suppression IS NULL;",
            new { idUtilisateur, relativePath }, cancellationToken: cancellationToken)) > 0;
    }
    public async Task<string?> GetPartnerPhotoPathAsync(Guid idUtilisateur, CancellationToken cancellationToken)
    {
        using var connection = _connectionFactory.CreateConnection(); await connection.OpenAsync(cancellationToken);
        return await connection.ExecuteScalarAsync<string?>(new CommandDefinition(
            "SELECT url_photo_profil FROM utilisateur WHERE id_utilisateur=@idUtilisateur AND type_compte='PARTENAIRE' AND date_suppression IS NULL;",
            new { idUtilisateur }, cancellationToken: cancellationToken));
    }
    public async Task<bool> UpdateTelephoneAsync(Guid idUtilisateur, string telephone, CancellationToken cancellationToken)
    {
        using var connection = _connectionFactory.CreateConnection();
        await connection.OpenAsync(cancellationToken);
        var rows = await connection.ExecuteAsync(new CommandDefinition(
            @"UPDATE utilisateur SET telephone=@telephone
              WHERE id_utilisateur=@idUtilisateur AND date_suppression IS NULL;",
            new { idUtilisateur, telephone }, cancellationToken: cancellationToken));
        return rows > 0;
    }
    public async Task<bool> AnonymizeAsync(Guid idUtilisateur, CancellationToken cancellationToken)
    {
        using var connection = _connectionFactory.CreateConnection(); await connection.OpenAsync(cancellationToken);
        var rows = await connection.ExecuteAsync(new CommandDefinition(
            @"UPDATE utilisateur SET nom='Compte supprimé', telephone=CASE WHEN telephone IS NULL THEN NULL ELSE '+000'||replace(id_utilisateur::text,'-','') END, email=NULL, google_subject=NULL,
                     notifications_rdv=FALSE, notifications_marketing=FALSE, consentement_donnees=FALSE,
                     est_suspendu=TRUE, date_suppression=now()
              WHERE id_utilisateur=@idUtilisateur AND date_suppression IS NULL;",
            new { idUtilisateur }, cancellationToken: cancellationToken));
        return rows > 0;
    }

    public async Task<int> GetLoyaltyPointsAsync(Guid idUtilisateur, CancellationToken cancellationToken)
    {
        using var connection=_connectionFactory.CreateConnection();await connection.OpenAsync(cancellationToken);
        return await connection.ExecuteScalarAsync<int>(new CommandDefinition("SELECT COALESCE(points_fidelite,0) FROM utilisateur WHERE id_utilisateur=@idUtilisateur;",new{idUtilisateur},cancellationToken:cancellationToken));
    }}


