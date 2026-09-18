namespace KekeBeauty.Application.Moderation;

public sealed class SuspendUtilisateurUseCase
{
    private readonly IModerationRepository _repository;

    public SuspendUtilisateurUseCase(IModerationRepository repository)
    {
        _repository = repository;
    }

    public Task<bool> ExecuteAsync(Guid idUtilisateur, bool suspendu, CancellationToken cancellationToken) =>
        _repository.SetUtilisateurSuspenduAsync(idUtilisateur, suspendu, cancellationToken);
}
