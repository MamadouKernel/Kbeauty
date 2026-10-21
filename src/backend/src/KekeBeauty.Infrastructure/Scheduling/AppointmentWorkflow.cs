using KekeBeauty.Infrastructure.Persistence;
using KekeBeauty.Modules.Scheduling.Domain;
using KekeBeauty.SharedKernel;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Npgsql;

namespace KekeBeauty.Infrastructure.Scheduling;

/// <summary>Errors shared by the booking, agenda and maintenance services.</summary>
public static class SchedulingErrors
{
    public static readonly Error EstablishmentNotFound = new("establishment.not_found", "Établissement introuvable.");
    public static readonly Error AppointmentNotFound = new("appointment.not_found", "Rendez-vous introuvable.");
    public static readonly Error BookingClosed = new("booking.closed", "La réservation en ligne n'est pas disponible pour cet établissement.");
    public static readonly Error SlotTaken = new("slot.taken", "Ce créneau vient d'être réservé. Choisissez-en un autre.");
    public static readonly Error SlotUnavailable = new("slot.unavailable", "Ce créneau n'est pas disponible.");
    public static readonly Error InvalidTransition = new("appointment.invalid_transition", "Cette action n'est pas possible dans l'état actuel du rendez-vous.");
    public static readonly Error TooLate = new("appointment.too_late", "Il est trop tard pour annuler ce rendez-vous en ligne : contactez directement l'établissement.");

    public static readonly Error RescheduleLimit = new("appointment.reschedule_limit", "Vous avez déjà déplacé ce rendez-vous le nombre de fois autorisé : contactez l'établissement.");

    public static Error Invalid(string message) => new("validation.failed", message);

    public static Error FeeNotAccepted(decimal fee) =>
        new("appointment.fee_not_accepted", $"Annuler maintenant retient {fee:0} F CFA sur votre paiement (frais d'annulation tardive). Confirmez pour continuer.");
}

/// <summary>
/// Applies state-machine transitions, writes the status history and sends the notifications and live updates
/// that go with each change (PRD RDV-03/04, AGD-06). Callers save their own changes first.
/// </summary>
public sealed class AppointmentWorkflow(
    KekeBeautyDbContext db,
    NotificationService notifications,
    IRealtimeNotifier realtime,
    IServiceProvider services,
    TimeProvider time,
    ILogger<AppointmentWorkflow> logger)
{
    public const int RequestValidityHours = 24;

    /// <summary>Validates and records a transition. Nothing is saved: the caller owns the unit of work.</summary>
    public Result Apply(Appointment appointment, AppointmentStatus to, ActorKind actor, Guid? actorUserId, string? comment = null)
    {
        if (!AppointmentStateMachine.CanTransition(appointment.Status, to, actor))
        {
            return Result.Failure(SchedulingErrors.InvalidTransition);
        }

        db.AppointmentHistory.Add(new AppointmentStatusHistory
        {
            AppointmentId = appointment.Id,
            FromStatus = appointment.Status,
            ToStatus = to,
            ActorUserId = actorUserId,
            At = time.GetUtcNow(),
            Comment = comment,
        });
        appointment.Status = to;
        return Result.Success();
    }

    /// <summary>
    /// A lost race on a slot: either the exclusion constraint fired (23P01) or, when two inserts conflict with each
    /// other at the same instant, PostgreSQL aborted one of them as a deadlock (40P01). Both mean "slot taken".
    /// EF may wrap the database error (e.g. as a transient-failure <see cref="InvalidOperationException"/>), so the
    /// whole exception chain is inspected.
    /// </summary>
    public static bool IsOverlapViolation(Exception exception)
    {
        for (var current = exception; current is not null; current = current.InnerException)
        {
            if (current is PostgresException { SqlState: PostgresErrorCodes.ExclusionViolation or PostgresErrorCodes.DeadlockDetected })
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>Notifies the people concerned and refreshes open calendars after a (saved) change.</summary>
    public async Task AfterChangeAsync(Appointment appointment, AppointmentStatus? from, DateTimeOffset? previousStart, CancellationToken ct)
    {
        var info = await db.Establishments.AsNoTracking()
            .Where(e => e.Id == appointment.EstablishmentId)
            .Select(e => new { e.Name, e.OwnerUserId, e.Timezone })
            .FirstAsync(ct);
        var client = appointment.ClientUserId is { } clientUserId
            ? await db.Users.AsNoTracking().Where(u => u.Id == clientUserId)
                .Select(u => new { u.FirstName, u.LastName }).FirstAsync(ct)
            : null;
        var zone = ZoneOf(info.Timezone);
        var clientName = appointment.WalkInName ?? $"{client?.FirstName} {client?.LastName}".Trim();

        // A slot held for a client who has not paid yet: the calendar changes, but nobody is told and nothing is shown to the partner.
        if (appointment.AwaitingPayment)
        {
            await realtime.SlotsChangedAsync(appointment.EstablishmentId, DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(appointment.StartsAt, zone).DateTime), ct);
            if (services.GetService(typeof(KekeBeauty.Infrastructure.Billing.AppointmentPaymentService)) is KekeBeauty.Infrastructure.Billing.AppointmentPaymentService held)
            {
                await held.AfterTransitionAsync(appointment, ct);
            }

            return;
        }

        var moved = from is { } same && same == appointment.Status && previousStart is { } was && was != appointment.StartsAt;
        var message = Compose(appointment, from, info.Name, clientName, zone, moved);
        var shown = TimeZoneInfo.ConvertTime(
            appointment.Status == AppointmentStatus.RescheduleProposed ? appointment.ProposedStartsAt ?? appointment.StartsAt : appointment.StartsAt,
            zone);
        var variables = new Dictionary<string, string>
        {
            ["etablissement"] = info.Name,
            ["client"] = clientName,
            ["date"] = shown.ToString("dd/MM/yyyy", System.Globalization.CultureInfo.InvariantCulture),
            ["heure"] = shown.ToString("HH:mm", System.Globalization.CultureInfo.InvariantCulture),
            ["motif"] = appointment.RejectionReason ?? string.Empty,
        };
        if (message.ForClient is { } forClient && appointment.ClientUserId is { } recipient)
        {
            await notifications.NotifyAsync(recipient, message.Template, forClient.Title, forClient.Body, appointment.ClientPhone, ct, variables);
        }

        if (message.ForPartner is { } forPartner && info.OwnerUserId is { } owner)
        {
            await notifications.NotifyAsync(owner, message.Template + ".partner", forPartner.Title, forPartner.Body, null, ct, variables);
        }

        await realtime.SlotsChangedAsync(appointment.EstablishmentId, DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(appointment.StartsAt, zone).DateTime), ct);
        if (previousStart is { } before && before != appointment.StartsAt)
        {
            await realtime.SlotsChangedAsync(appointment.EstablishmentId, DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(before, zone).DateTime), ct);
        }

        var kind = from is null
            ? AppointmentEvents.Requested
            : appointment.Status is AppointmentStatus.CancelledByClient or AppointmentStatus.CancelledByEstablishment
                or AppointmentStatus.Rejected or AppointmentStatus.Expired
                ? AppointmentEvents.Cancelled
                : AppointmentEvents.Updated;
        await realtime.AppointmentChangedAsync(appointment.EstablishmentId, kind, appointment.Id, ct);

        // A paid appointment that will not take place gives the money back to the person who paid.
        if (services.GetService(typeof(KekeBeauty.Infrastructure.Billing.AppointmentPaymentService)) is KekeBeauty.Infrastructure.Billing.AppointmentPaymentService refunds)
        {
            await refunds.AfterTransitionAsync(appointment, ct);
        }

        // A freed slot is offered to the clients waiting for that day; a failure here must never undo the change itself.
        if (services.GetService(typeof(KekeBeauty.Infrastructure.Growth.WaitlistService)) is KekeBeauty.Infrastructure.Growth.WaitlistService waitlist)
        {
            try
            {
                await waitlist.AfterChangeAsync(appointment, from, ct);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogWarning(ex, "Waiting list notification failed for appointment {AppointmentId}", appointment.Id);
            }
        }
    }

    /// <summary>Tells the owner of the establishment about something that is not a status change (e.g. the client is running late).</summary>
    public async Task NotifyOwnerAsync(Appointment appointment, string template, string title, string body, IReadOnlyDictionary<string, string> variables, CancellationToken ct)
    {
        var info = await db.Establishments.AsNoTracking()
            .Where(e => e.Id == appointment.EstablishmentId)
            .Select(e => new { e.Name, e.OwnerUserId, e.Timezone })
            .FirstAsync(ct);
        if (info.OwnerUserId is not { } owner)
        {
            return;
        }

        var shown = TimeZoneInfo.ConvertTime(appointment.StartsAt, ZoneOf(info.Timezone));
        var client = appointment.ClientUserId is { } clientUserId
            ? await db.Users.AsNoTracking().Where(u => u.Id == clientUserId).Select(u => new { u.FirstName, u.LastName }).FirstAsync(ct)
            : null;
        var all = new Dictionary<string, string>(variables)
        {
            ["etablissement"] = info.Name,
            ["client"] = appointment.WalkInName ?? $"{client?.FirstName} {client?.LastName}".Trim(),
            ["date"] = shown.ToString("dd/MM/yyyy", System.Globalization.CultureInfo.InvariantCulture),
            ["heure"] = shown.ToString("HH:mm", System.Globalization.CultureInfo.InvariantCulture),
            ["motif"] = string.Empty,
        };
        await notifications.NotifyAsync(owner, template, title, body, null, ct, all);
    }

    public static TimeZoneInfo ZoneOf(string timezoneId)
    {
        try
        {
            return TimeZoneInfo.FindSystemTimeZoneById(timezoneId);
        }
        catch (Exception ex) when (ex is TimeZoneNotFoundException or InvalidTimeZoneException)
        {
            // Containers without tzdata: fall back to UTC, which is the offset of Abidjan (no daylight saving).
            return TimeZoneInfo.Utc;
        }
    }

    private sealed record Text(string Title, string Body);

    private sealed record Composed(string Template, Text? ForClient, Text? ForPartner);

    private static Composed Compose(Appointment a, AppointmentStatus? from, string establishment, string client, TimeZoneInfo zone, bool moved)
    {
        var when = Describe(a.StartsAt, zone);

        // Same status, new start: the client moved the appointment to another slot.
        if (moved)
        {
            return new Composed(
                "appointment.rescheduled_by_client",
                new Text("Rendez-vous déplacé", $"Votre rendez-vous chez {establishment} est maintenant {when}."),
                new Text("Rendez-vous déplacé", $"{client} a déplacé son rendez-vous : {when}."));
        }

        if (from is null)
        {
            return a.Status == AppointmentStatus.Confirmed
                ? new Composed(
                    "appointment.confirmed",
                    new Text("Rendez-vous confirmé", $"Votre rendez-vous chez {establishment} {when} est confirmé."),
                    new Text("Nouveau rendez-vous", $"{client} a réservé {when} (confirmation automatique)."))
                : new Composed(
                    "appointment.requested",
                    new Text("Demande envoyée", $"Votre demande chez {establishment} {when} a bien été envoyée. Vous serez prévenu(e) dès sa validation."),
                    new Text("Nouvelle demande de rendez-vous", $"{client} souhaite réserver {when}. Répondez depuis votre agenda."));
        }

        return a.Status switch
        {
            AppointmentStatus.Confirmed when from == AppointmentStatus.RescheduleProposed => new Composed(
                "appointment.reschedule_accepted",
                new Text("Rendez-vous reprogrammé", $"Votre rendez-vous chez {establishment} est confirmé {when}."),
                new Text("Reprogrammation acceptée", $"{client} a accepté le nouveau créneau : {when}.")),
            AppointmentStatus.Confirmed => new Composed(
                "appointment.confirmed",
                new Text("Rendez-vous confirmé", $"Votre rendez-vous chez {establishment} {when} est confirmé."),
                null),
            AppointmentStatus.Rejected => new Composed(
                "appointment.rejected",
                new Text("Demande refusée", $"{establishment} ne peut pas vous recevoir {when}.{Reason(a.RejectionReason)}"),
                null),
            AppointmentStatus.Expired => new Composed(
                "appointment.expired",
                new Text("Demande expirée", $"{establishment} n'a pas répondu à votre demande {when}. Le créneau est libéré : réessayez ou choisissez un autre horaire."),
                new Text("Demande expirée", $"La demande de {client} {when} a expiré sans réponse.")),
            AppointmentStatus.CancelledByEstablishment => new Composed(
                "appointment.cancelled_by_establishment",
                new Text("Rendez-vous annulé", $"{establishment} a annulé votre rendez-vous {when}.{Reason(a.RejectionReason)}"),
                null),
            AppointmentStatus.CancelledByClient => new Composed(
                "appointment.cancelled_by_client",
                null,
                new Text("Rendez-vous annulé", $"{client} a annulé son rendez-vous {when}.")),
            AppointmentStatus.RescheduleProposed => new Composed(
                "appointment.reschedule_proposed",
                new Text(
                    "Nouveau créneau proposé",
                    $"{establishment} vous propose {Describe(a.ProposedStartsAt ?? a.StartsAt, zone)} à la place. Acceptez ou refusez dans Mes rendez-vous."),
                null),
            _ => new Composed("appointment.updated", null, null),
        };

        string Reason(string? reason) => string.IsNullOrWhiteSpace(reason) ? string.Empty : $" Motif : {reason}";
    }

    private static string Describe(DateTimeOffset instant, TimeZoneInfo zone)
    {
        var local = TimeZoneInfo.ConvertTime(instant, zone);
        return $"le {local:dd/MM/yyyy} à {local:HH:mm}";
    }
}
