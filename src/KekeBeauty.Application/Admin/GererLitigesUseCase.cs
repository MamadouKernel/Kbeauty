using KekeBeauty.Application.Rdv;

namespace KekeBeauty.Application.Admin;

/// <summary>Feature 018 (Parcours 6) : instruction et resolution des litiges par l'administrateur.</summary>
public sealed class GererLitigesUseCase
{
    private readonly ILitigeRepository _repository;

    public GererLitigesUseCase(ILitigeRepository repository)
    {
        _repository = repository;
    }

    public Task<IReadOnlyList<LitigeDto>> ListerAsync(string? statut, CancellationToken cancellationToken) =>
        _repository.ListerAsync(statut, cancellationToken);

    public Task<bool> ResoudreAsync(Guid idLitige, string resolution, CancellationToken cancellationToken) =>
        _repository.ResoudreAsync(idLitige, resolution, cancellationToken);
}
