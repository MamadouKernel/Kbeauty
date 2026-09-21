namespace KekeBeauty.Application.Notifications;
public sealed class ClientNotificationService
{
    private readonly INotificationRepository _notifications; private readonly IPushSubscriptionRepository _subscriptions; private readonly IPushNotificationSender _sender;
    public ClientNotificationService(INotificationRepository notifications, IPushSubscriptionRepository subscriptions, IPushNotificationSender sender){_notifications=notifications;_subscriptions=subscriptions;_sender=sender;}
    public async Task SendAsync(Guid userId,string title,string message,Guid? rdvId,string url,CancellationToken ct)
    {
        await _notifications.CreerAsync(userId,title,message,rdvId,ct);
        foreach(var subscription in await _subscriptions.ListerParUtilisateurAsync(userId,ct))
        { var (success,expired)=await _sender.EnvoyerAsync(subscription,title,message,url,ct); if(!success&&expired) await _subscriptions.SupprimerAsync(subscription.Endpoint,ct); }
    }
}
