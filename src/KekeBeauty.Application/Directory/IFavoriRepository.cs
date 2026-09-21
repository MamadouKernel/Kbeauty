namespace KekeBeauty.Application.Directory;

/// <summary>Parcours 1 : favoris client. Ferme le gap laisse par le bouton "coeur" purement
/// visuel/local de la fiche etablissement (EtablissementDetailPage.razor).</summary>
public interface IFavoriRepository
{
    Task<IReadOnlyList<EtablissementSummary>> ListerAsync(Guid idUtilisateurClient, CancellationToken cancellationToken);

    Task<bool> EstFavoriAsync(Guid idUtilisateurClient, Guid idEtablissement, CancellationToken cancellationToken);

    /// <summary>Bascule l'etat et retourne le nouvel etat (true = ajoute, false = retire).</summary>
    Task<bool> ToggleAsync(Guid idUtilisateurClient, Guid idEtablissement, CancellationToken cancellationToken);
}

