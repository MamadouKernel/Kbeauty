using Dapper;
using KekeBeauty.Application.Rdv;

namespace KekeBeauty.Infrastructure.Rdv;

public sealed class AvisRepository : IAvisRepository
{
    private readonly IDbConnectionFactory _connectionFactory;

    public AvisRepository(IDbConnectionFactory connectionFactory)
    {
        _connectionFactory = connectionFactory;
    }

    public async Task<Guid?> CreerAsync(Guid idRdv, short note, string? commentaire, CancellationToken cancellationToken)
    {
        using var connection = _connectionFactory.CreateConnection();
        await connection.OpenAsync(cancellationToken);

        // ON CONFLICT DO NOTHING : idempotent, garantit FR-003 (un seul avis par RDV) sans
        // depasser une simple contrainte UNIQUE deja posee en base.
        return await connection.ExecuteScalarAsync<Guid?>(new CommandDefinition(
            @"INSERT INTO avis (id_rdv, note, commentaire)
              VALUES (@idRdv, @note, @commentaire)
              ON CONFLICT (id_rdv) DO NOTHING
              RETURNING id_avis;",
            new { idRdv, note, commentaire },
            cancellationToken: cancellationToken));
    }

    public async Task<bool> ExisteAsync(Guid idRdv, CancellationToken cancellationToken)
    {
        using var connection = _connectionFactory.CreateConnection();
        await connection.OpenAsync(cancellationToken);

        var count = await connection.ExecuteScalarAsync<int>(new CommandDefinition(
            "SELECT count(*) FROM avis WHERE id_rdv = @idRdv;",
            new { idRdv },
            cancellationToken: cancellationToken));

        return count > 0;
    }

    public async Task<AvisEtablissement> ListerParEtablissementAsync(Guid idEtablissement, CancellationToken cancellationToken)
    {
        using var connection = _connectionFactory.CreateConnection();
        await connection.OpenAsync(cancellationToken);

        var rows = await connection.QueryAsync<AvisItem>(new CommandDefinition(
            @"SELECT a.id_rdv AS IdRdv, a.note AS Note, a.commentaire AS Commentaire, a.date_creation AS DateCreation, (a.url_photo_avant IS NOT NULL) AS HasPhotoAvant, (a.url_photo_apres IS NOT NULL) AS HasPhotoApres
              FROM avis a
              JOIN rdv r ON r.id_rdv = a.id_rdv
              WHERE r.id_etablissement = @idEtablissement
              ORDER BY a.date_creation DESC;",
            new { idEtablissement },
            cancellationToken: cancellationToken));

        var avis = rows.AsList();

        return new AvisEtablissement
        {
            Avis = avis,
            NombreAvis = avis.Count,
            NoteMoyenne = avis.Count > 0 ? avis.Average(a => a.Note) : null,
        };
    }

    public async Task<(bool Saved,bool BonusAwarded)> SaveBeforeAfterAsync(Guid idRdv,Guid idClient,string beforePath,string afterPath,bool sharePublicly,CancellationToken cancellationToken)
    {
        using var connection=_connectionFactory.CreateConnection();await connection.OpenAsync(cancellationToken);using var transaction=connection.BeginTransaction();
        var saved=await connection.ExecuteAsync(new CommandDefinition(@"UPDATE avis a SET url_photo_avant=@beforePath,url_photo_apres=@afterPath,photos_partage_public=@sharePublicly FROM rdv r
            WHERE a.id_rdv=r.id_rdv AND a.id_rdv=@idRdv AND r.id_utilisateur_client=@idClient AND r.statut_rdv='TERMINE';",new{idRdv,idClient,beforePath,afterPath,sharePublicly},transaction,cancellationToken:cancellationToken))>0;
        if(!saved){transaction.Rollback();return(false,false);}
        var awarded=await connection.ExecuteAsync(new CommandDefinition(@"INSERT INTO fidelite_mouvement(id_utilisateur,id_rdv,type_mouvement,points,libelle)
            VALUES(@idClient,@idRdv,'PHOTOS_AVANT_APRES',200,'Galerie avant/après') ON CONFLICT DO NOTHING;",new{idClient,idRdv},transaction,cancellationToken:cancellationToken))>0;
        if(awarded)await connection.ExecuteAsync(new CommandDefinition("UPDATE utilisateur SET points_fidelite=points_fidelite+200 WHERE id_utilisateur=@idClient;",new{idClient},transaction,cancellationToken:cancellationToken));
        transaction.Commit();return(true,awarded);
    }

    public async Task<string?> GetPhotoPathAsync(Guid idRdv,string type,CancellationToken cancellationToken)
    {
        using var connection=_connectionFactory.CreateConnection();await connection.OpenAsync(cancellationToken);
        return await connection.ExecuteScalarAsync<string?>(new CommandDefinition("SELECT CASE WHEN @type='avant' THEN url_photo_avant WHEN @type='apres' THEN url_photo_apres END FROM avis WHERE id_rdv=@idRdv AND photos_partage_public=true;",new{idRdv,type},cancellationToken:cancellationToken));
    }}
