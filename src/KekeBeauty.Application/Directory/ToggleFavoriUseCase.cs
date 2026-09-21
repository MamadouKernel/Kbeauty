namespace KekeBeauty.Application.Directory;

public sealed class ToggleFavoriUseCase
{
    private readonly IFavoriRepository _repository;

    public ToggleFavoriUseCase(IFavoriRepository repository)
    {
        _repository = repository;
    }

    public Task<bool> ExecuteAsync(Guid idUtilisateurClient, Guid idEtablissement, CancellationToken cancellationToken) =>
        _repository.ToggleAsync(idUtilisateurClient, idEtablissement, cancellationToken);
}
