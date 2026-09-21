using Dapper;
using KekeBeauty.Application.Partner;

namespace KekeBeauty.Infrastructure.Partner;

public sealed class PartnerPrestationRepository : IPartnerPrestationRepository
{
    private readonly IDbConnectionFactory _connectionFactory;

    public PartnerPrestationRepository(IDbConnectionFactory connectionFactory)
    {
        _connectionFactory = connectionFactory;
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

    public async Task<IReadOnlyList<EtablissementGereRow>> GetEtablissementsByGerantAsync(Guid idGerant, CancellationToken cancellationToken)
    {
        using var connection = _connectionFactory.CreateConnection();
        await connection.OpenAsync(cancellationToken);

        var rows = await connection.QueryAsync<EtablissementGereRow>(new CommandDefinition(
            @"SELECT id_etablissement AS IdEtablissement, nom_etablissement AS NomEtablissement,
                     statut_kyc AS StatutKyc, est_suspendu AS EstSuspendu
              FROM etablissement WHERE id_utilisateur_gerant = @idGerant
              ORDER BY nom_etablissement;",
            new { idGerant },
            cancellationToken: cancellationToken));

        return rows.AsList();
    }

    public async Task<Guid> AddAsync(Guid idEtablissement, string libelle, decimal tarif, short dureeMinutes, CancellationToken cancellationToken)
    {
        using var connection = _connectionFactory.CreateConnection();
        await connection.OpenAsync(cancellationToken);

        return await connection.ExecuteScalarAsync<Guid>(new CommandDefinition(
            @"INSERT INTO prestation (libelle_prestation, tarif, duree_minutes, id_etablissement)
              VALUES (@libelle, @tarif, @dureeMinutes, @idEtablissement) RETURNING id_prestation;",
            new { libelle, tarif, dureeMinutes, idEtablissement },
            cancellationToken: cancellationToken));
    }

    public async Task<bool> UpdateAsync(Guid idEtablissement, Guid idPrestation, string libelle, decimal tarif, short dureeMinutes, CancellationToken cancellationToken)
    {
        using var connection = _connectionFactory.CreateConnection();
        await connection.OpenAsync(cancellationToken);

        var rows = await connection.ExecuteAsync(new CommandDefinition(
            @"UPDATE prestation SET libelle_prestation = @libelle, tarif = @tarif, duree_minutes = @dureeMinutes
              WHERE id_prestation = @idPrestation AND id_etablissement = @idEtablissement;",
            new { libelle, tarif, dureeMinutes, idPrestation, idEtablissement },
            cancellationToken: cancellationToken));

        return rows > 0;
    }

    public async Task<bool> DeleteAsync(Guid idEtablissement, Guid idPrestation, CancellationToken cancellationToken)
    {
        using var connection = _connectionFactory.CreateConnection();
        await connection.OpenAsync(cancellationToken);

        var rows = await connection.ExecuteAsync(new CommandDefinition(
            "DELETE FROM prestation WHERE id_prestation = @idPrestation AND id_etablissement = @idEtablissement;",
            new { idPrestation, idEtablissement },
            cancellationToken: cancellationToken));

        return rows > 0;
    }

    public async Task<bool> UpdateModePaiementServiceAsync(Guid idEtablissement, string modePaiementService, bool paiementWave, bool paiementOrangeMoney, bool paiementMoovMoney, CancellationToken cancellationToken)
    {
        using var connection = _connectionFactory.CreateConnection();
        await connection.OpenAsync(cancellationToken);

        var rows = await connection.ExecuteAsync(new CommandDefinition(
            @"UPDATE etablissement SET mode_paiement_service = @modePaiementService,
                paiement_wave = @paiementWave, paiement_orange_money = @paiementOrangeMoney,
                paiement_moov_money = @paiementMoovMoney WHERE id_etablissement = @idEtablissement;",
            new { idEtablissement, modePaiementService, paiementWave, paiementOrangeMoney, paiementMoovMoney }, cancellationToken: cancellationToken));
        return rows > 0;
    }

    public async Task<bool> UpdateProfilBoutiqueAsync(Guid idEtablissement, string nom, string? description,
        string telephone, decimal latitude, decimal longitude, string? horaires, CancellationToken cancellationToken)
    {
        using var connection = _connectionFactory.CreateConnection();
        await connection.OpenAsync(cancellationToken);
        var rows = await connection.ExecuteAsync(new CommandDefinition(
            @"UPDATE etablissement SET nom_etablissement=@nom, description=@description,
                numero_service_client=@telephone, gps_latitude=@latitude, gps_longitude=@longitude,
                horaires=CASE WHEN @horaires IS NULL OR @horaires = '' THEN NULL ELSE to_jsonb(@horaires::text) END
                WHERE id_etablissement=@idEtablissement;",
            new { idEtablissement, nom, description, telephone, latitude, longitude, horaires },
            cancellationToken: cancellationToken));
        return rows > 0;
    }

    public async Task<IReadOnlyList<IndisponibiliteRow>> ListIndisponibilitesAsync(Guid idEtablissement, CancellationToken cancellationToken)
    {
        using var connection = _connectionFactory.CreateConnection(); await connection.OpenAsync(cancellationToken);
        var rows = await connection.QueryAsync<IndisponibiliteRow>(new CommandDefinition(
            @"SELECT id_indisponibilite AS IdIndisponibilite, date_debut AS DateDebut, date_fin AS DateFin, motif AS Motif
              FROM indisponibilite_etablissement WHERE id_etablissement=@idEtablissement AND date_fin >= now()
              ORDER BY date_debut;", new { idEtablissement }, cancellationToken: cancellationToken));
        return rows.AsList();
    }

    public async Task<Guid> AddIndisponibiliteAsync(Guid idEtablissement, DateTimeOffset debut, DateTimeOffset fin, string? motif, CancellationToken cancellationToken)
    {
        using var connection = _connectionFactory.CreateConnection(); await connection.OpenAsync(cancellationToken);
        return await connection.ExecuteScalarAsync<Guid>(new CommandDefinition(
            @"INSERT INTO indisponibilite_etablissement(id_etablissement,date_debut,date_fin,motif)
              VALUES(@idEtablissement,@debut,@fin,@motif) RETURNING id_indisponibilite;",
            new { idEtablissement, debut, fin, motif }, cancellationToken: cancellationToken));
    }

    public async Task<bool> DeleteIndisponibiliteAsync(Guid idEtablissement, Guid idIndisponibilite, CancellationToken cancellationToken)
    {
        using var connection = _connectionFactory.CreateConnection(); await connection.OpenAsync(cancellationToken);
        return await connection.ExecuteAsync(new CommandDefinition(
            "DELETE FROM indisponibilite_etablissement WHERE id_etablissement=@idEtablissement AND id_indisponibilite=@idIndisponibilite;",
            new { idEtablissement, idIndisponibilite }, cancellationToken: cancellationToken)) > 0;
    }

    public async Task<Guid> AddMediaAsync(Guid idEtablissement, string path, short ordre, CancellationToken ct)
    { using var c=_connectionFactory.CreateConnection();await c.OpenAsync(ct);return await c.ExecuteScalarAsync<Guid>(new CommandDefinition("INSERT INTO media(type_media,url,ordre_affichage,id_etablissement) VALUES('PHOTO',@path,@ordre,@idEtablissement) RETURNING id_media;",new{idEtablissement,path,ordre},cancellationToken:ct)); }
    public async Task<bool> DeleteMediaAsync(Guid idEtablissement, Guid idMedia, CancellationToken ct)
    { using var c=_connectionFactory.CreateConnection();await c.OpenAsync(ct);return await c.ExecuteAsync(new CommandDefinition("DELETE FROM media WHERE id_etablissement=@idEtablissement AND id_media=@idMedia;",new{idEtablissement,idMedia},cancellationToken:ct))>0; }
    public async Task<string?> GetMediaPathAsync(Guid idMedia, CancellationToken ct)
    { using var c=_connectionFactory.CreateConnection();await c.OpenAsync(ct);return await c.ExecuteScalarAsync<string?>(new CommandDefinition("SELECT url FROM media WHERE id_media=@idMedia;",new{idMedia},cancellationToken:ct)); }
}
