using KekeBeauty.Application.Rdv;

namespace KekeBeauty.Application.Partner;

/// <summary>Feature 018 (Parcours 5) : le gerant assigne une collaboratrice de son etablissement a
/// un RDV (planning individuel). idCollaborateur == null retire l'assignation.</summary>
public sealed class AssignerCollaborateurUseCase
{
    private readonly IRdvRepository _rdvRepository;
    private readonly ICollaborateurRepository _collaborateurRepository;

    public AssignerCollaborateurUseCase(IRdvRepository rdvRepository, ICollaborateurRepository collaborateurRepository)
    {
        _rdvRepository = rdvRepository;
        _collaborateurRepository = collaborateurRepository;
    }

    public async Task<string> ExecuteAsync(Guid idEtablissement, Guid idRdv, Guid? idCollaborateur, CancellationToken cancellationToken)
    {
        if (idCollaborateur is not null &&
            !await _collaborateurRepository.AppartientAEtablissementAsync(idCollaborateur.Value, idEtablissement, cancellationToken))
        {
            return "collaboratrice_invalide";
        }

        var updated = await _rdvRepository.AssignerCollaborateurAsync(idEtablissement, idRdv, idCollaborateur, cancellationToken);
        return updated ? "assigned" : "rdv_introuvable";
    }
}
