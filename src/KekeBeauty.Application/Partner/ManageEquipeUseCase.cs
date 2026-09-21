namespace KekeBeauty.Application.Partner;

public sealed class ManageEquipeUseCase
{
    private readonly ICollaborateurRepository _repository;

    public ManageEquipeUseCase(ICollaborateurRepository repository)
    {
        _repository = repository;
    }

    public Task<Guid> AjouterAsync(Guid idEtablissement, string nom, string? specialite, string? telephone, CancellationToken cancellationToken) =>
        _repository.AjouterAsync(idEtablissement, nom, specialite, telephone, cancellationToken);

    public Task<IReadOnlyList<CollaborateurDto>> ListerAsync(Guid idEtablissement, CancellationToken cancellationToken) =>
        _repository.ListerAsync(idEtablissement, cancellationToken);

    public Task<bool> RetirerAsync(Guid idEtablissement, Guid idCollaborateur, CancellationToken cancellationToken) =>
        _repository.RetirerAsync(idEtablissement, idCollaborateur, cancellationToken);
}
