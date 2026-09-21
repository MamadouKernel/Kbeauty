namespace KekeBeauty.Application.Partner;

/// <summary>Feature 018 (Parcours 5) : appele par AuthController juste apres une verification OTP
/// reussie pour TypeCompte.Collaborateur, pour rattacher le compte utilisateur (cree ou retrouve
/// generiquement par VerifyOtpUseCase) a la fiche collaboratrice deja referencee par le gerant avec
/// le meme numero. Ne cree jamais de fiche collaboratrice : seul le gerant peut le faire.</summary>
public sealed class LinkCollaborateurCompteUseCase
{
    private readonly ICollaborateurRepository _repository;

    public LinkCollaborateurCompteUseCase(ICollaborateurRepository repository)
    {
        _repository = repository;
    }

    public async Task<Guid?> ExecuteAsync(string telephone, Guid idUtilisateur, CancellationToken cancellationToken)
    {
        var deja = await _repository.GetIdCollaborateurByUtilisateurAsync(idUtilisateur, cancellationToken);
        if (deja is not null)
        {
            return deja;
        }

        return await _repository.LinkUtilisateurByTelephoneAsync(telephone, idUtilisateur, cancellationToken);
    }
}
