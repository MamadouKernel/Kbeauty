namespace KekeBeauty.Application.Notifications;

public sealed class NotificationCenterUseCase
{
    private readonly INotificationRepository _repository;

    public NotificationCenterUseCase(INotificationRepository repository)
    {
        _repository = repository;
    }

    public Task<IReadOnlyList<NotificationDto>> ListerAsync(Guid idUtilisateur, CancellationToken cancellationToken) =>
        _repository.ListerAsync(idUtilisateur, cancellationToken);

    public Task<int> CompterNonLuesAsync(Guid idUtilisateur, CancellationToken cancellationToken) =>
        _repository.CompterNonLuesAsync(idUtilisateur, cancellationToken);

    public Task MarquerToutesLuesAsync(Guid idUtilisateur, CancellationToken cancellationToken) =>
        _repository.MarquerToutesLuesAsync(idUtilisateur, cancellationToken);
}
