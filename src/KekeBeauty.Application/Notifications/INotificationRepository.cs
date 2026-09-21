namespace KekeBeauty.Application.Notifications;

public sealed class NotificationDto
{
    public Guid IdNotification { get; set; }
    public string Titre { get; set; } = string.Empty;
    public string Message { get; set; } = string.Empty;
    public Guid? IdRdv { get; set; }
    public bool Lu { get; set; }
    public DateTimeOffset DateCreation { get; set; }
}

public interface INotificationRepository
{
    Task CreerAsync(Guid idUtilisateur, string titre, string message, Guid? idRdv, CancellationToken cancellationToken);

    Task<IReadOnlyList<NotificationDto>> ListerAsync(Guid idUtilisateur, CancellationToken cancellationToken);

    Task<int> CompterNonLuesAsync(Guid idUtilisateur, CancellationToken cancellationToken);

    Task MarquerToutesLuesAsync(Guid idUtilisateur, CancellationToken cancellationToken);
}
