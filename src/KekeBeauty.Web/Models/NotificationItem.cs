namespace KekeBeauty.Web.Models;

public sealed class NotificationItem
{
    public Guid IdNotification { get; set; }
    public string Titre { get; set; } = string.Empty;
    public string Message { get; set; } = string.Empty;
    public Guid? IdRdv { get; set; }
    public bool Lu { get; set; }
    public DateTimeOffset DateCreation { get; set; }
}
