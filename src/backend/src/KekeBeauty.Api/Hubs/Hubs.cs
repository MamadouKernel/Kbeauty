using System.Globalization;
using KekeBeauty.Infrastructure.Persistence;
using KekeBeauty.SharedKernel;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;

namespace KekeBeauty.Api.Hubs;

public static class HubRoutes
{
    public const string Availability = "/hubs/availability";
    public const string Pro = "/hubs/pro";
    public const string Notifications = "/hubs/notifications";

    public static string EstablishmentGroup(Guid establishmentId) => $"establishment:{establishmentId}";

    public static string UserGroup(Guid userId) => $"user:{userId}";
}

/// <summary>Anonymous: anyone looking at a calendar hears when a slot of that establishment changes (PRD RDV-02).</summary>
public sealed class AvailabilityHub : Hub
{
    public Task Join(Guid establishmentId) =>
        Groups.AddToGroupAsync(Context.ConnectionId, HubRoutes.EstablishmentGroup(establishmentId));

    public Task Leave(Guid establishmentId) =>
        Groups.RemoveFromGroupAsync(Context.ConnectionId, HubRoutes.EstablishmentGroup(establishmentId));
}

/// <summary>Partner agenda: new requests and cancellations arrive live (PRD AGD-06). Owners only.</summary>
[Authorize(Policy = "Partner")]
public sealed class ProHub(KekeBeautyDbContext db) : Hub
{
    public async Task Join(Guid establishmentId)
    {
        var userId = Guid.Parse(Context.User!.FindFirst("sub")!.Value);
        var owns = await db.Establishments.AnyAsync(e => e.Id == establishmentId && e.OwnerUserId == userId, Context.ConnectionAborted);
        if (!owns)
        {
            throw new HubException("Établissement introuvable.");
        }

        await Groups.AddToGroupAsync(Context.ConnectionId, HubRoutes.EstablishmentGroup(establishmentId));
    }

    public Task Leave(Guid establishmentId) =>
        Groups.RemoveFromGroupAsync(Context.ConnectionId, HubRoutes.EstablishmentGroup(establishmentId));
}

/// <summary>Each signed-in connection joins its own user group, so notifications reach every open tab.</summary>
[Authorize]
public sealed class NotificationsHub : Hub
{
    public override async Task OnConnectedAsync()
    {
        var userId = Guid.Parse(Context.User!.FindFirst("sub")!.Value);
        await Groups.AddToGroupAsync(Context.ConnectionId, HubRoutes.UserGroup(userId));
        await base.OnConnectedAsync();
    }
}

public sealed class SignalRRealtimeNotifier(
    IHubContext<AvailabilityHub> availability,
    IHubContext<ProHub> pro,
    IHubContext<NotificationsHub> notifications) : IRealtimeNotifier
{
    public Task SlotsChangedAsync(Guid establishmentId, DateOnly date, CancellationToken cancellationToken = default) =>
        availability.Clients.Group(HubRoutes.EstablishmentGroup(establishmentId))
            .SendAsync("SlotsChanged", date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture), cancellationToken);

    public Task AppointmentChangedAsync(Guid establishmentId, string kind, Guid appointmentId, CancellationToken cancellationToken = default) =>
        pro.Clients.Group(HubRoutes.EstablishmentGroup(establishmentId)).SendAsync(kind, appointmentId, cancellationToken);

    public Task NotificationReceivedAsync(Guid userId, NotificationPayload notification, CancellationToken cancellationToken = default) =>
        notifications.Clients.Group(HubRoutes.UserGroup(userId)).SendAsync("NotificationReceived", notification, cancellationToken);
}
