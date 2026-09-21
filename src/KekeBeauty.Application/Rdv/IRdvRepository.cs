namespace KekeBeauty.Application.Rdv;

public interface IRdvRepository
{
    Task<IReadOnlyList<CreneauOccupe>> GetCreneauxOccupesAsync(Guid idEtablissement, DateOnly date, CancellationToken cancellationToken);

    /// <summary>Retourne null si l'etablissement n'est pas VALIDE ou la prestation n'existe pas ;
    /// retourne un Guid si cree ; Guid.Empty si chevauchement detecte (voir research.md Decision 1).</summary>
    Task<Guid?> CreateIfNoOverlapAsync(
        Guid idEtablissement, Guid idPrestation, Guid idUtilisateurClient, DateTimeOffset dateHeureDebut, string? modePaiementChoisi, CancellationToken cancellationToken);

    Task<Guid?> GetOwnerIdAsync(Guid idEtablissement, CancellationToken cancellationToken);

    Task<string?> GetClientTelephoneAsync(Guid idRdv, CancellationToken cancellationToken);

    Task<string?> GetClientNomAsync(Guid idRdv, CancellationToken cancellationToken);

    /// <summary>Feature 018 : id_utilisateur_client du RDV, utilise pour creer une notification
    /// in-app (independante du canal SMS/WhatsApp deja porte par GetClientTelephoneAsync).</summary>
    Task<Guid?> GetClientIdAsync(Guid idRdv, CancellationToken cancellationToken);

    Task<DateTimeOffset?> GetDateHeureDebutAsync(Guid idRdv, CancellationToken cancellationToken);

    Task<bool> SignalerRetardAsync(Guid idRdv, Guid idUtilisateurClient, short minutes, CancellationToken cancellationToken);

    Task<bool> UpdateStatutAsync(Guid idEtablissement, Guid idRdv, string statutRdv, CancellationToken cancellationToken);

    /// <summary>Reprogramme si le nouveau creneau ne chevauche pas un autre RDV (FR-007).</summary>
    Task<bool> RescheduleAsync(Guid idEtablissement, Guid idRdv, DateTimeOffset nouvelleDateHeureDebut, CancellationToken cancellationToken);

    /// <summary>Feature 010 (frontend) : permet au client de relire le statut d'un RDV deja cree.
    /// Retourne null si introuvable ou si idUtilisateurClient n'est pas le proprietaire (meme
    /// reponse pour les deux cas au niveau controleur - pas de fuite d'information).</summary>
    Task<RdvStatutRow?> GetStatutAsync(Guid idRdv, Guid idUtilisateurClient, CancellationToken cancellationToken);

    /// <summary>Feature 011 (frontend partenaire) : liste toutes les demandes de RDV d'un
    /// etablissement, tous statuts, pour que le gerant puisse les traiter.</summary>
    Task<IReadOnlyList<RdvPartenaireRow>> ListByEtablissementAsync(Guid idEtablissement, CancellationToken cancellationToken);

    Task<(Guid? IdRdv, string Status)> CreateWalkInAsync(Guid idEtablissement, Guid idPrestation, Guid? idCollaborateur, string nomClient, string telephoneClient, DateTimeOffset dateHeureDebut, string modePaiement, CancellationToken cancellationToken);

    /// <summary>Feature 013 : historique complet (tous statuts) des RDV d'un client, trie du plus
    /// recent au plus ancien, avec le statut de paiement en ligne s'il y en a eu un.</summary>
    Task<IReadOnlyList<RdvHistoriqueRow>> ListByClientAsync(Guid idUtilisateurClient, CancellationToken cancellationToken);

    /// <summary>Feature 015 (US2) : annulation par le client lui-meme. Ne fait rien et retourne false
    /// si le RDV n'appartient pas a ce client ou n'est pas au statut DEMANDE/CONFIRME (FR-003/004).</summary>
    Task<bool> AnnulerParClientAsync(Guid idRdv, Guid idUtilisateurClient, CancellationToken cancellationToken);

    /// <summary>Feature 013 : tarif de la prestation au moment de la demande de RDV, utilise comme
    /// montant du paiement en ligne optionnel. Null si la prestation n'existe pas.</summary>
    Task<decimal?> GetTarifPrestationAsync(Guid idPrestation, CancellationToken cancellationToken);

    Task<string?> GetModePaiementServiceRdvAsync(Guid idRdv, CancellationToken cancellationToken);

    /// <summary>Feature 018 (Parcours 5) : assigne (ou desassigne si idCollaborateur est null) une
    /// collaboratrice a un RDV. Retourne false si le RDV n'appartient pas a cet etablissement.</summary>
    Task<bool> AssignerCollaborateurAsync(Guid idEtablissement, Guid idRdv, Guid? idCollaborateur, CancellationToken cancellationToken);

    /// <summary>Feature 018 (Parcours 3) : passe atomiquement au statut TERMINE tout RDV CONFIRME
    /// dont l'heure de fin (date_heure_debut + duree de la prestation) est deja passee. Ferme un
    /// gap reel : sans ce job, aucun mecanisme ne faisait jamais transitionner un RDV vers TERMINE,
    /// rendant avis et pourboire inaccessibles en usage normal.</summary>
    Task<IReadOnlyList<RdvTermineInfo>> MarquerRdvsExpiresCommeTerminesAsync(CancellationToken cancellationToken);
    Task<IReadOnlyList<RdvReminderInfo>> ListerRappelsAsync(CancellationToken cancellationToken);
}

public sealed class RdvReminderInfo { public Guid IdRdv { get; set; } public Guid IdUtilisateurClient { get; set; } public string NomEtablissement { get; set; } = string.Empty; public DateTimeOffset DateHeureDebut { get; set; } }

public sealed class RdvTermineInfo
{
    public Guid IdRdv { get; set; }
    public Guid IdUtilisateurClient { get; set; }
    public Guid? IdCollaborateur { get; set; }
    public string NomEtablissement { get; set; } = string.Empty;
    public DateTimeOffset DateHeureDebut { get; set; }
}

public sealed class RdvHistoriqueRow
{
    public Guid IdRdv { get; set; }
    public short? RetardMinutes { get; set; }
    public DateTimeOffset? DateSignalementRetard { get; set; }
    public Guid IdEtablissement { get; set; }
    public string NomEtablissement { get; set; } = string.Empty;
    public string LibellePrestation { get; set; } = string.Empty;
    public DateTimeOffset DateHeureDebut { get; set; }
    public string StatutRdv { get; set; } = string.Empty;
    public string? StatutPaiement { get; set; }
    public string ModePaiementService { get; set; } = "ESPECES";
    public bool ADejaAvis { get; set; }
    public Guid? IdCollaborateur { get; set; }
}

public sealed class RdvStatutRow
{
    public Guid IdRdv { get; set; }
    public string StatutRdv { get; set; } = string.Empty;
    public DateTimeOffset DateHeureDebut { get; set; }
}

public sealed class RdvPartenaireRow
{
    public Guid IdRdv { get; set; }
    public string StatutRdv { get; set; } = string.Empty;
    public DateTimeOffset DateHeureDebut { get; set; }
    public string LibellePrestation { get; set; } = string.Empty;
    public string TelephoneClient { get; set; } = string.Empty;
    public string NomClient { get; set; } = string.Empty;
    public short DureeMinutes { get; set; }
    public decimal TarifPrestation { get; set; }
    public string OrigineRdv { get; set; } = "EN_LIGNE";
    public Guid? IdCollaborateur { get; set; }
    public short? RetardMinutes { get; set; }
    public DateTimeOffset? DateSignalementRetard { get; set; }
}

