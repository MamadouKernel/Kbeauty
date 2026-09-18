namespace KekeBeauty.Application.Moderation;

public sealed class SuspendEtablissementUseCase
{
    private readonly IModerationRepository _repository;

    public SuspendEtablissementUseCase(IModerationRepository repository)
    {
        _repository = repository;
    }

    public Task<bool> ExecuteAsync(Guid idEtablissement, bool suspendu, CancellationToken cancellationToken) =>
        _repository.SetEtablissementSuspenduAsync(idEtablissement, suspendu, cancellationToken);
}
