using System.Globalization;
using KekeBeauty.Infrastructure.Partner;
using KekeBeauty.Infrastructure.Persistence;
using KekeBeauty.Infrastructure.Scheduling;
using KekeBeauty.Modules.Catalog.Domain;
using KekeBeauty.Modules.Scheduling.Domain;
using KekeBeauty.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace KekeBeauty.Infrastructure.Growth;

public sealed record GroupBookingDto(
    Guid Id,
    Guid EstablishmentId,
    string EstablishmentName,
    string EstablishmentSlug,
    string ClientName,
    string? ClientPhone,
    string EventType,
    DateTimeOffset DesiredStartsAt,
    int GroupSize,
    string Services,
    string? Message,
    string Status,
    decimal? QuotedPrice,
    string? PartnerMessage,
    DateTimeOffset CreatedAt,
    DateTimeOffset? RespondedAt);

public sealed record GroupBookingInput(Guid EstablishmentId, string? EventType, DateTimeOffset DesiredStartsAt, int GroupSize, string? Services, string? Message, string? Phone);

public sealed record GroupBookingAnswerInput(bool Accept, decimal? QuotedPrice, string? Message);

/// <summary>
/// Group bookings (a wedding, a birthday): too many people and services for the slot grid, so it is a request the
/// establishment answers — a quote and a message, or a refusal — before arranging the day directly with the client.
/// </summary>
public sealed class GroupBookingService(KekeBeautyDbContext db, NotificationService notifications, TimeProvider time)
{
    public const int MinGroupSize = 2;
    public const int MaxGroupSize = 50;
    public const int MaxOpenRequestsPerClient = 5;

    private static readonly Error NotFound = new("group_booking.not_found", "Demande de groupe introuvable.");
    private static readonly Error AlreadyAnswered = new("group_booking.already_answered", "Cette demande a déjà reçu une réponse.");
    private static readonly Error NotCancellable = new("group_booking.not_cancellable", "Cette demande ne peut plus être annulée.");
    private static readonly Error TooMany = new("group_booking.too_many", $"Vous avez déjà {MaxOpenRequestsPerClient} demandes de groupe en attente de réponse.");

    // ───────── Client ─────────

    public async Task<Result<GroupBookingDto>> CreateAsync(Guid clientId, GroupBookingInput input, CancellationToken ct)
    {
        if (!Enum.TryParse<GroupEventType>(input.EventType, ignoreCase: true, out var eventType) || !Enum.IsDefined(eventType))
        {
            return Result.Failure<GroupBookingDto>(SchedulingErrors.Invalid("Type d'événement inconnu."));
        }

        var services = input.Services?.Trim();
        var message = string.IsNullOrWhiteSpace(input.Message) ? null : input.Message.Trim();
        var phone = PartnerText.NormalizePhone(input.Phone);
        if (input.GroupSize is < MinGroupSize or > MaxGroupSize)
        {
            return Result.Failure<GroupBookingDto>(SchedulingErrors.Invalid($"Un groupe compte de {MinGroupSize} à {MaxGroupSize} personnes."));
        }

        if (services is not { Length: >= 3 and <= 500 } || message is { Length: > 1000 })
        {
            return Result.Failure<GroupBookingDto>(SchedulingErrors.Invalid("Décrivez les prestations souhaitées (3 à 500 caractères) ; le message est limité à 1 000 caractères."));
        }

        if (phone is null)
        {
            return Result.Failure<GroupBookingDto>(SchedulingErrors.Invalid("Numéro de téléphone invalide : l'établissement vous rappellera pour organiser la journée."));
        }

        var establishment = await db.Establishments.AsNoTracking()
            .Where(e => e.Id == input.EstablishmentId && e.Status == EstablishmentStatus.Active)
            .Select(e => new { e.Id, e.Name, e.OwnerUserId, e.Timezone, e.MaxHorizonDays })
            .FirstOrDefaultAsync(ct);
        if (establishment is null)
        {
            return Result.Failure<GroupBookingDto>(SchedulingErrors.EstablishmentNotFound);
        }

        // Weddings are planned months ahead: the window is wider than for a single appointment.
        var now = time.GetUtcNow();
        if (input.DesiredStartsAt < now.AddHours(24) || input.DesiredStartsAt > now.AddDays(Math.Max(establishment.MaxHorizonDays, 365)))
        {
            return Result.Failure<GroupBookingDto>(SchedulingErrors.Invalid("Choisissez une date au moins 24 heures à l'avance et dans les 12 prochains mois."));
        }

        if (await db.GroupBookingRequests.CountAsync(r => r.ClientUserId == clientId && r.Status == GroupBookingStatus.Pending, ct) >= MaxOpenRequestsPerClient)
        {
            return Result.Failure<GroupBookingDto>(TooMany);
        }

        var request = new GroupBookingRequest
        {
            EstablishmentId = establishment.Id,
            ClientUserId = clientId,
            EventType = eventType,
            DesiredStartsAt = input.DesiredStartsAt,
            GroupSize = input.GroupSize,
            Services = services,
            Message = message,
            Phone = phone,
            CreatedAt = now,
        };
        db.GroupBookingRequests.Add(request);
        await db.SaveChangesAsync(ct);

        if (establishment.OwnerUserId is { } owner)
        {
            var local = TimeZoneInfo.ConvertTime(input.DesiredStartsAt, AppointmentWorkflow.ZoneOf(establishment.Timezone));
            await notifications.NotifyAsync(owner, "group_booking.requested", "Nouvelle demande de groupe",
                $"Une demande pour {input.GroupSize} personnes ({EventLabel(eventType)}) le {local.ToString("dd/MM/yyyy", CultureInfo.InvariantCulture)} à {local.ToString("HH:mm", CultureInfo.InvariantCulture)}. Répondez depuis la page Demandes.",
                null, ct);
        }

        return Result.Success((await ProjectAsync([request], forPartner: false, ct))[0]);
    }

    public async Task<IReadOnlyList<GroupBookingDto>> MineAsync(Guid clientId, CancellationToken ct) =>
        await ProjectAsync(
            await db.GroupBookingRequests.AsNoTracking().Where(r => r.ClientUserId == clientId).OrderByDescending(r => r.CreatedAt).Take(50).ToListAsync(ct),
            forPartner: false,
            ct);

    public async Task<Result<GroupBookingDto>> CancelAsync(Guid clientId, Guid requestId, CancellationToken ct)
    {
        var request = await db.GroupBookingRequests.FirstOrDefaultAsync(r => r.Id == requestId && r.ClientUserId == clientId, ct);
        if (request is null)
        {
            return Result.Failure<GroupBookingDto>(NotFound);
        }

        if (request.Status is not (GroupBookingStatus.Pending or GroupBookingStatus.Accepted))
        {
            return Result.Failure<GroupBookingDto>(NotCancellable);
        }

        var wasAccepted = request.Status == GroupBookingStatus.Accepted;
        request.Status = GroupBookingStatus.CancelledByClient;
        await db.SaveChangesAsync(ct);

        var owner = await db.Establishments.AsNoTracking().Where(e => e.Id == request.EstablishmentId).Select(e => e.OwnerUserId).FirstAsync(ct);
        if (owner is not null)
        {
            await notifications.NotifyAsync(owner.Value, "group_booking.cancelled", "Demande de groupe annulée",
                wasAccepted ? "Une cliente a annulé l'événement de groupe que vous aviez accepté." : "Une cliente a retiré sa demande de groupe.", null, ct);
        }

        return Result.Success((await ProjectAsync([request], forPartner: false, ct))[0]);
    }

    // ───────── Establishment ─────────

    public async Task<Result<IReadOnlyList<GroupBookingDto>>> ForEstablishmentAsync(Guid ownerId, Guid establishmentId, CancellationToken ct)
    {
        if (!await PartnerSupport.OwnedBy(db, ownerId, establishmentId).AnyAsync(ct))
        {
            return Result.Failure<IReadOnlyList<GroupBookingDto>>(PartnerErrors.NotFound);
        }

        // Requests waiting for an answer first, then the most recent.
        var requests = await db.GroupBookingRequests.AsNoTracking().Where(r => r.EstablishmentId == establishmentId)
            .OrderBy(r => r.Status == GroupBookingStatus.Pending ? 0 : 1).ThenByDescending(r => r.CreatedAt).Take(200).ToListAsync(ct);
        return Result.Success(await ProjectAsync(requests, forPartner: true, ct));
    }

    public async Task<Result<GroupBookingDto>> AnswerAsync(Guid ownerId, Guid requestId, GroupBookingAnswerInput input, CancellationToken ct)
    {
        var found = await (from r in db.GroupBookingRequests
                           join e in db.Establishments on r.EstablishmentId equals e.Id
                           where r.Id == requestId && e.OwnerUserId == ownerId
                           select new { Request = r, e.Name }).FirstOrDefaultAsync(ct);
        if (found is null)
        {
            return Result.Failure<GroupBookingDto>(NotFound);
        }

        var request = found.Request;
        if (request.Status != GroupBookingStatus.Pending)
        {
            return Result.Failure<GroupBookingDto>(AlreadyAnswered);
        }

        var message = input.Message?.Trim();
        if (message is not { Length: >= 3 and <= 1000 })
        {
            return Result.Failure<GroupBookingDto>(PartnerErrors.Invalid("Votre message doit compter de 3 à 1 000 caractères."));
        }

        if (input.Accept && input.QuotedPrice is not (> 0 and <= 100_000_000))
        {
            return Result.Failure<GroupBookingDto>(PartnerErrors.Invalid("Indiquez le prix total proposé pour le groupe."));
        }

        request.Status = input.Accept ? GroupBookingStatus.Accepted : GroupBookingStatus.Declined;
        request.QuotedPrice = input.Accept ? decimal.Round(input.QuotedPrice!.Value, 0) : null;
        request.PartnerMessage = message;
        request.RespondedAt = time.GetUtcNow();
        await db.SaveChangesAsync(ct);

        await notifications.NotifyAsync(request.ClientUserId, input.Accept ? "group_booking.accepted" : "group_booking.declined",
            input.Accept ? "Votre demande de groupe est acceptée" : "Votre demande de groupe n'est pas possible",
            input.Accept
                ? $"{found.Name} accepte votre événement pour {request.GroupSize} personnes, pour {request.QuotedPrice:0} F au total. Message : {message}"
                : $"{found.Name} ne peut pas accueillir votre groupe. Message : {message}",
            request.Phone, ct);
        return Result.Success((await ProjectAsync([request], forPartner: true, ct))[0]);
    }

    // ───────── Projection ─────────

    public static string EventLabel(GroupEventType type) => type switch
    {
        GroupEventType.Wedding => "mariage",
        GroupEventType.Birthday => "anniversaire",
        GroupEventType.Baptism => "baptême",
        GroupEventType.Corporate => "événement d'entreprise",
        _ => "événement",
    };

    private async Task<IReadOnlyList<GroupBookingDto>> ProjectAsync(IReadOnlyList<GroupBookingRequest> requests, bool forPartner, CancellationToken ct)
    {
        if (requests.Count == 0)
        {
            return [];
        }

        var establishmentIds = requests.Select(r => r.EstablishmentId).Distinct().ToList();
        var clientIds = requests.Select(r => r.ClientUserId).Distinct().ToList();
        var establishments = await db.Establishments.AsNoTracking().Where(e => establishmentIds.Contains(e.Id)).ToDictionaryAsync(e => e.Id, e => new { e.Name, e.Slug }, ct);
        var clients = await db.Users.AsNoTracking().Where(u => clientIds.Contains(u.Id)).ToDictionaryAsync(u => u.Id, u => $"{u.FirstName} {u.LastName}".Trim(), ct);
        return requests.Select(r =>
        {
            var e = establishments[r.EstablishmentId];
            return new GroupBookingDto(
                r.Id, r.EstablishmentId, e.Name, e.Slug,
                clients.GetValueOrDefault(r.ClientUserId) ?? "Cliente",
                forPartner ? r.Phone : null,
                r.EventType.ToString(), r.DesiredStartsAt, r.GroupSize, r.Services, r.Message, r.Status.ToString(),
                r.QuotedPrice, r.PartnerMessage, r.CreatedAt, r.RespondedAt);
        }).ToList();
    }
}
