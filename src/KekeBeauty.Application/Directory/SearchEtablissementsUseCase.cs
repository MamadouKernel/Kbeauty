namespace KekeBeauty.Application.Directory;

public sealed class SearchEtablissementsUseCase
{
    private readonly IDirectoryRepository _repository;

    public SearchEtablissementsUseCase(IDirectoryRepository repository)
    {
        _repository = repository;
    }

    public Task<IReadOnlyList<EtablissementSummary>> ExecuteAsync(string? categorie, string? commune, CancellationToken cancellationToken) =>
        _repository.SearchAsync(categorie, commune, cancellationToken);
}
