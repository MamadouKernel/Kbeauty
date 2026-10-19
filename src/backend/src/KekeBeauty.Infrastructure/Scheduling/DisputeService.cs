using KekeBeauty.Infrastructure.Admin;
using KekeBeauty.Infrastructure.Billing;
using KekeBeauty.Infrastructure.Partner;
using KekeBeauty.Infrastructure.Persistence;
using KekeBeauty.Modules.Billing.Domain;
using KekeBeauty.Modules.Scheduling.Domain;
using KekeBeauty.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace KekeBeauty.Infrastructure.Scheduling;

public sealed record DisputeDto(
    Guid Id,
    Guid AppointmentId,
    Guid EstablishmentId,
    string EstablishmentName,
    string ClientName,
    DateTimeOffset AppointmentStartsAt,
    string Reason,
    string Description,
    string Status,
    string? Resolution,
    string? PartnerResponse,
    string? AdminNote,
    string? RefundStatus,
    DateTimeOffset CreatedAt,
    DateTimeOffset? ResolvedAt);

public sealed record OpenDisputeInput(Guid AppointmentId, string? Reason, string? Description);

public sealed record DisputeResponseInput(string? Message);

public sealed record DisputeResolveInput(string? Resolution, string? Note);

/// <summary>
/// Keke Protect: a client can file a claim about a past appointment; the establishment answers; an administrator decides.
/// While a claim is open the establishment is not paid out for that appointment, and an upheld claim refunds the client.
/// </summary>
public sealed class DisputeService(
    KekeBeautyDbContext db,
    AppointmentPaymentService payments,
    NotificationService notifications,
    TimeProvider time)
{
    public const int MinTextLength = 10;
    public const int MaxTextLength = 1000;

    /// <summary>How long after an appointment ended a client can still file a claim.</summary>
    public static readonly TimeSpan ClaimWindow = TimeSpan.FromDays(7);

    private static readonly Error NotFound = new("dispute.not_found", "Litige introuvable.");
    private static readonly Error NotDisputable = new("dispute.not_disputable", "Ce rendez-vous ne peut pas faire l'objet d'un litige : il doit être passé, depuis moins de 7 jours.");
    private static readonly Error AlreadyFiled = new("dispute.already_filed", "Un litige a déjà été ouvert pour ce rendez-vous.");
    private static readonly Error AlreadyResolved = new("dispute.already_resolved", "Ce litige est déjà clos.");

    // ───────── Client ─────────

    public async Task<Result<DisputeDto>> OpenAsync(Guid clientId, OpenDisputeInput input, CancellationToken ct)
    {
        if (!Enum.TryParse<DisputeReason>(input.Reason, ignoreCase: true, out var reason) || !Enum.IsDefined(reason))
        {
            return Result.Failure<DisputeDto>(SchedulingErrors.Invalid("Motif inconnu."));
        }

        var description = input.Description?.Trim();
        if (description is not { Length: >= MinTextLength and <= MaxTextLength })
        {
            return Result.Failure<DisputeDto>(SchedulingErrors.Invalid($"Décrivez le problème en {MinTextLength} à {MaxTextLength} caractères."));
        }

        var appointment = await db.Appointments.AsNoTracking().FirstOrDefaultAsync(a => a.Id == input.AppointmentId && a.ClientUserId == clientId, ct);
        if (appointment is null)
        {
            return Result.Failure<DisputeDto>(SchedulingErrors.AppointmentNotFound);
        }

        var now = time.GetUtcNow();
        var happened = appointment.Status is AppointmentStatus.Completed or AppointmentStatus.NoShow or AppointmentStatus.Confirmed;
        if (!happened || appointment.AwaitingPayment || appointment.EndsAt > now || now - appointment.EndsAt > ClaimWindow)
        {
            return Result.Failure<DisputeDto>(NotDisputable);
        }

        if (await db.Disputes.AnyAsync(d => d.AppointmentId == appointment.Id, ct))
        {
            return Result.Failure<DisputeDto>(AlreadyFiled);
        }

        var dispute = new Dispute
        {
            AppointmentId = appointment.Id,
            EstablishmentId = appointment.EstablishmentId,
            ClientUserId = clientId,
            Reason = reason,
            Description = description,
            CreatedAt = now,
        };
        db.Disputes.Add(dispute);
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException)
        {
            // Two simultaneous claims: the unique index on the appointment lets only one through.
            db.ChangeTracker.Clear();
            return Result.Failure<DisputeDto>(AlreadyFiled);
        }

        var owner = await db.Establishments.AsNoTracking().Where(e => e.Id == appointment.EstablishmentId).Select(e => new { e.OwnerUserId, e.Name }).FirstAsync(ct);
        if (owner.OwnerUserId is not null)
        {
            await notifications.NotifyAsync(owner.OwnerUserId.Value, "dispute.opened", "Un litige a été ouvert",
                $"Une cliente a signalé un problème sur un rendez-vous chez {owner.Name}. Répondez-lui depuis la page Litiges ; le versement correspondant est suspendu en attendant.", null, ct);
        }

        return Result.Success((await ProjectAsync([dispute], ct))[0]);
    }

    public async Task<IReadOnlyList<DisputeDto>> MineAsync(Guid clientId, CancellationToken ct) =>
        await ProjectAsync(await db.Disputes.AsNoTracking().Where(d => d.ClientUserId == clientId).OrderByDescending(d => d.CreatedAt).Take(100).ToListAsync(ct), ct);

    // ───────── Establishment ─────────

    public async Task<Result<IReadOnlyList<DisputeDto>>> ForEstablishmentAsync(Guid ownerId, Guid establishmentId, CancellationToken ct)
    {
        if (!await PartnerSupport.OwnedBy(db, ownerId, establishmentId).AnyAsync(ct))
        {
            return Result.Failure<IReadOnlyList<DisputeDto>>(PartnerErrors.NotFound);
        }

        var disputes = await db.Disputes.AsNoTracking().Where(d => d.EstablishmentId == establishmentId).OrderByDescending(d => d.CreatedAt).Take(100).ToListAsync(ct);
        return Result.Success(await ProjectAsync(disputes, ct));
    }

    public async Task<Result<DisputeDto>> RespondAsync(Guid ownerId, Guid disputeId, DisputeResponseInput input, CancellationToken ct)
    {
        var dispute = await (from d in db.Disputes
                             join e in db.Establishments on d.EstablishmentId equals e.Id
                             where d.Id == disputeId && e.OwnerUserId == ownerId
                             select d).FirstOrDefaultAsync(ct);
        if (dispute is null)
        {
            return Result.Failure<DisputeDto>(NotFound);
        }

        if (dispute.Status != DisputeStatus.Open)
        {
            return Result.Failure<DisputeDto>(AlreadyResolved);
        }

        var message = input.Message?.Trim();
        if (message is not { Length: >= 3 and <= MaxTextLength })
        {
            return Result.Failure<DisputeDto>(SchedulingErrors.Invalid($"Votre réponse doit compter de 3 à {MaxTextLength} caractères."));
        }

        dispute.PartnerResponse = message;
        dispute.PartnerRespondedAt = time.GetUtcNow();
        await db.SaveChangesAsync(ct);
        await notifications.NotifyAsync(dispute.ClientUserId, "dispute.answered", "L'établissement a répondu",
            "L'établissement a répondu à votre litige. Notre équipe examine le dossier et vous informera de sa décision.", null, ct);
        return Result.Success((await ProjectAsync([dispute], ct))[0]);
    }

    // ───────── Back-office ─────────

    public async Task<Result<IReadOnlyList<DisputeDto>>> ListAsync(string? status, CancellationToken ct)
    {
        var query = db.Disputes.AsNoTracking().AsQueryable();
        if (status is not null)
        {
            if (!Enum.TryParse<DisputeStatus>(status, ignoreCase: true, out var wanted) || !Enum.IsDefined(wanted))
            {
                return Result.Failure<IReadOnlyList<DisputeDto>>(AdminErrors.Invalid("Statut inconnu (Open ou Resolved)."));
            }

            query = query.Where(d => d.Status == wanted);
        }

        // Open claims first, oldest first: they are waiting for a decision.
        var disputes = await query.OrderBy(d => d.Status).ThenBy(d => d.CreatedAt).Take(200).ToListAsync(ct);
        return Result.Success(await ProjectAsync(disputes, ct));
    }

    public async Task<Result<DisputeDto>> ResolveAsync(AdminContext admin, Guid disputeId, DisputeResolveInput input, CancellationToken ct)
    {
        if (!Enum.TryParse<DisputeResolution>(input.Resolution, ignoreCase: true, out var resolution) || !Enum.IsDefined(resolution))
        {
            return Result.Failure<DisputeDto>(AdminErrors.Invalid("Décision inconnue (RefundedClient ou RejectedClaim)."));
        }

        var note = AdminErrors.CleanReason(input.Note);
        if (note is null)
        {
            return Result.Failure<DisputeDto>(AdminErrors.ReasonRequired);
        }

        var dispute = await db.Disputes.FirstOrDefaultAsync(d => d.Id == disputeId, ct);
        if (dispute is null)
        {
            return Result.Failure<DisputeDto>(NotFound);
        }

        if (dispute.Status != DisputeStatus.Open)
        {
            return Result.Failure<DisputeDto>(AlreadyResolved);
        }

        var appointment = await db.Appointments.FirstAsync(a => a.Id == dispute.AppointmentId, ct);
        AppointmentPayment? paid = null;
        if (resolution == DisputeResolution.RefundedClient)
        {
            paid = await db.AppointmentPayments
                .Where(p => p.AppointmentId == appointment.Id && p.Status == AppointmentPaymentStatus.Paid)
                .OrderByDescending(p => p.CreatedAt).FirstOrDefaultAsync(ct);
        }

        dispute.Status = DisputeStatus.Resolved;
        dispute.Resolution = resolution;
        dispute.AdminNote = note;
        dispute.ResolvedBy = admin.ActorId;
        dispute.ResolvedAt = time.GetUtcNow();
        Audit.Add(db, admin, time, "dispute.resolve", "Dispute", dispute.Id,
            new { resolution = resolution.ToString(), refund = paid?.Amount, alreadyPaidOut = paid?.PayoutId != null });
        await db.SaveChangesAsync(ct);

        if (paid is not null)
        {
            await payments.RefundAsync(paid, appointment, ct);
        }

        var outcome = resolution == DisputeResolution.RefundedClient
            ? (paid is null ? "Vous serez remboursé(e) directement par l'établissement, aucun paiement en ligne n'ayant été fait." : "Votre paiement en ligne vous est remboursé.")
            : "Le litige est rejeté.";
        await notifications.NotifyAsync(dispute.ClientUserId, "dispute.resolved", "Votre litige a été examiné", $"Décision : {outcome} Message de l'équipe : {note}", null, ct);
        var owner = await db.Establishments.AsNoTracking().Where(e => e.Id == dispute.EstablishmentId).Select(e => e.OwnerUserId).FirstAsync(ct);
        if (owner is not null)
        {
            await notifications.NotifyAsync(owner.Value, "dispute.resolved", "Un litige a été clos",
                resolution == DisputeResolution.RefundedClient ? "Le litige a été tranché en faveur de la cliente." : "Le litige a été rejeté : votre versement est de nouveau possible.", null, ct);
        }

        return Result.Success((await ProjectAsync([dispute], ct))[0]);
    }

    // ───────── Projection ─────────

    private async Task<IReadOnlyList<DisputeDto>> ProjectAsync(IReadOnlyList<Dispute> disputes, CancellationToken ct)
    {
        if (disputes.Count == 0)
        {
            return [];
        }

        var appointmentIds = disputes.Select(d => d.AppointmentId).ToList();
        var establishmentIds = disputes.Select(d => d.EstablishmentId).Distinct().ToList();
        var clientIds = disputes.Select(d => d.ClientUserId).Distinct().ToList();
        var starts = await db.Appointments.AsNoTracking().Where(a => appointmentIds.Contains(a.Id)).ToDictionaryAsync(a => a.Id, a => a.StartsAt, ct);
        var establishments = await db.Establishments.AsNoTracking().Where(e => establishmentIds.Contains(e.Id)).ToDictionaryAsync(e => e.Id, e => e.Name, ct);
        var clients = await db.Users.AsNoTracking().Where(u => clientIds.Contains(u.Id)).ToDictionaryAsync(u => u.Id, u => $"{u.FirstName} {u.LastName}".Trim(), ct);
        var refunds = (await db.AppointmentPayments.AsNoTracking().Where(p => appointmentIds.Contains(p.AppointmentId)).OrderBy(p => p.CreatedAt).ToListAsync(ct))
            .GroupBy(p => p.AppointmentId).ToDictionary(g => g.Key, g => g.Last().Status.ToString());

        return disputes.Select(d => new DisputeDto(
            d.Id,
            d.AppointmentId,
            d.EstablishmentId,
            establishments.GetValueOrDefault(d.EstablishmentId) ?? string.Empty,
            clients.GetValueOrDefault(d.ClientUserId) ?? "Cliente",
            starts.GetValueOrDefault(d.AppointmentId),
            d.Reason.ToString(),
            d.Description,
            d.Status.ToString(),
            d.Resolution?.ToString(),
            d.PartnerResponse,
            d.AdminNote,
            refunds.GetValueOrDefault(d.AppointmentId),
            d.CreatedAt,
            d.ResolvedAt)).ToList();
    }
}
