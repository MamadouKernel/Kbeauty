using Dapper;
using KekeBeauty.Application.Admin;

namespace KekeBeauty.Infrastructure.Admin;

public sealed class AdminStatsRepository : IAdminStatsRepository
{
    private readonly IDbConnectionFactory _connectionFactory;

    public AdminStatsRepository(IDbConnectionFactory connectionFactory)
    {
        _connectionFactory = connectionFactory;
    }

    public async Task<AdminStatistiquesGlobales> GetStatistiquesGlobalesAsync(CancellationToken cancellationToken)
    {
        using var connection = _connectionFactory.CreateConnection();
        await connection.OpenAsync(cancellationToken);

        // Dapper : convention du projet "classe mutable, jamais record positionnel" - on materialise
        // chaque agregation dans une petite classe dediee plutot qu'un tuple (mapping tuple multi-
        // colonnes non fiable avec Dapper, meme raison que le bug recurrent deja documente).
        var etablissements = await connection.QuerySingleAsync<EtablissementsRow>(new CommandDefinition(
            @"SELECT
                COUNT(*) FILTER (WHERE statut_kyc = 'VALIDE') AS Valides,
                COUNT(*) FILTER (WHERE statut_kyc = 'EN_ATTENTE') AS EnAttente,
                COUNT(*) FILTER (WHERE statut_kyc = 'REJETE') AS Rejetes
              FROM etablissement;",
            cancellationToken: cancellationToken));

        var rdv = await connection.QuerySingleAsync<RdvRow>(new CommandDefinition(
            @"SELECT
                COUNT(*) FILTER (WHERE r.statut_rdv = 'DEMANDE') AS Demande,
                COUNT(*) FILTER (WHERE r.statut_rdv = 'CONFIRME') AS Confirme,
                COUNT(*) FILTER (WHERE r.statut_rdv = 'TERMINE') AS Termine,
                COUNT(*) FILTER (WHERE r.statut_rdv IN ('REFUSE', 'ANNULE')) AS RefuseOuAnnule,
                COALESCE(SUM(p.tarif) FILTER (WHERE r.statut_rdv IN ('CONFIRME', 'TERMINE')), 0) AS RevenuEstime
              FROM rdv r
              JOIN prestation p ON p.id_prestation = r.id_prestation;",
            cancellationToken: cancellationToken));

        var abonnements = await connection.QuerySingleAsync<AbonnementsRow>(new CommandDefinition(
            @"SELECT
                COUNT(*) FILTER (WHERE statut_abonnement = 'ACTIF') AS Actifs,
                COUNT(*) FILTER (WHERE statut_abonnement = 'IMPAYE') AS Impayes
              FROM abonnement;",
            cancellationToken: cancellationToken));

        var avis = await connection.QuerySingleAsync<AvisRow>(new CommandDefinition(
            "SELECT count(*) AS Nombre, AVG(note) AS Moyenne FROM avis;",
            cancellationToken: cancellationToken));

        var nombreClients = await connection.ExecuteScalarAsync<int>(new CommandDefinition(
            "SELECT count(*) FROM utilisateur WHERE type_compte = 'CLIENT';",
            cancellationToken: cancellationToken));

        return new AdminStatistiquesGlobales
        {
            EtablissementsValides = etablissements.Valides,
            EtablissementsEnAttente = etablissements.EnAttente,
            EtablissementsRejetes = etablissements.Rejetes,
            RdvDemande = rdv.Demande,
            RdvConfirme = rdv.Confirme,
            RdvTermine = rdv.Termine,
            RdvRefuseOuAnnule = rdv.RefuseOuAnnule,
            RevenuEstimeTotal = rdv.RevenuEstime,
            AbonnementsActifs = abonnements.Actifs,
            AbonnementsImpayes = abonnements.Impayes,
            NombreAvis = avis.Nombre,
            NoteMoyenneGlobale = avis.Moyenne,
            NombreClients = nombreClients,
        };
    }

    private sealed class EtablissementsRow
    {
        public int Valides { get; set; }
        public int EnAttente { get; set; }
        public int Rejetes { get; set; }
    }

    private sealed class RdvRow
    {
        public int Demande { get; set; }
        public int Confirme { get; set; }
        public int Termine { get; set; }
        public int RefuseOuAnnule { get; set; }
        public decimal RevenuEstime { get; set; }
    }

    private sealed class AbonnementsRow
    {
        public int Actifs { get; set; }
        public int Impayes { get; set; }
    }

    private sealed class AvisRow
    {
        public int Nombre { get; set; }
        public double? Moyenne { get; set; }
    }
}
