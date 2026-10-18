using KekeBeauty.Infrastructure.Partner;
using KekeBeauty.Infrastructure.Persistence;
using KekeBeauty.Modules.Identity.Domain;
using KekeBeauty.Modules.Scheduling.Domain;
using KekeBeauty.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace KekeBeauty.Infrastructure.Scheduling;

/// <summary>Team side of the establishment: the owner links collaborators, the collaborator runs the day (portal, timer, technical notes).</summary>
public sealed class TeamService(KekeBeautyDbContext db, TimeProvider time)
{
    public const decimal MaxCommissionPercent = 100m;
    public const int MaxNoteLength = 2000;
    public const int MaxAllergiesLength = 500;

    /// <summary>A treatment can be started a little before its slot, never a day ahead.</summary>
    public static readonly TimeSpan EarlyStart = TimeSpan.FromMinutes(30);

    /// <summary>A treatment can still be started this long after its slot ended (running late), not weeks later.</summary>
    public static readonly TimeSpan LateStart = TimeSpan.FromHours(2);

    /// <summary>A treatment cannot be finished the instant it started: this blocks fabricated commissions.</summary>
    public static readonly TimeSpan MinDuration = TimeSpan.FromMinutes(1);

    private static readonly Error MemberNotFound = new("team.not_found", "Collaboratrice introuvable.");
    private static readonly Error UnknownAccount = new("team.account_not_found", "Aucun compte avec cette adresse e-mail : la collaboratrice doit d'abord créer un compte.");
    private static readonly Error NotEligible = new("team.not_eligible", "Ce compte ne peut pas devenir collaboratrice (propriétaire, administrateur ou compte suspendu).");
    private static readonly Error ClientNotFound = new("client.not_found", "Cliente introuvable dans cet établissement.");

    // ───────── Owner ─────────

    public async Task<Result<IReadOnlyList<TeamMemberDto>>> ListAsync(Guid ownerId, Guid establishmentId, CancellationToken ct)
    {
        if (!await Owns(ownerId, establishmentId, ct))
        {
            return Result.Failure<IReadOnlyList<TeamMemberDto>>(PartnerErrors.NotFound);
        }

        await ResourceService.EnsureDefaultAsync(db, establishmentId, time.GetUtcNow(), ct);
        var resources = await db.Resources.AsNoTracking().Where(r => r.EstablishmentId == establishmentId)
            .OrderBy(r => r.CreatedAt).ThenBy(r => r.Id).ToListAsync(ct);
        var userIds = resources.Where(r => r.UserId != null).Select(r => r.UserId!.Value).ToList();
        var users = await db.Users.AsNoTracking().Where(u => userIds.Contains(u.Id)).ToDictionaryAsync(u => u.Id, ct);

        return Result.Success<IReadOnlyList<TeamMemberDto>>(resources.Select(r =>
        {
            var user = r.UserId is { } id ? users.GetValueOrDefault(id) : null;
            return new TeamMemberDto(r.Id, r.Name, r.IsActive, r.UserId, user?.Email, user is null ? null : $"{user.FirstName} {user.LastName}".Trim(), r.CommissionPercent);
        }).ToList());
    }

    /// <summary>Gives a resource its collaborator account and commission rate; an empty e-mail only changes the rate.</summary>
    public async Task<Result<TeamMemberDto>> AssignAsync(Guid ownerId, Guid establishmentId, Guid resourceId, TeamAssignmentInput input, CancellationToken ct)
    {
        if (!await Owns(ownerId, establishmentId, ct))
        {
            return Result.Failure<TeamMemberDto>(PartnerErrors.NotFound);
        }

        var resource = await db.Resources.FirstOrDefaultAsync(r => r.Id == resourceId && r.EstablishmentId == establishmentId, ct);
        if (resource is null)
        {
            return Result.Failure<TeamMemberDto>(MemberNotFound);
        }

        if (input.CommissionPercent is < 0 or > MaxCommissionPercent)
        {
            return Result.Failure<TeamMemberDto>(SchedulingErrors.Invalid("La commission doit être comprise entre 0 et 100 %."));
        }

        resource.CommissionPercent = decimal.Round(input.CommissionPercent, 2);
        var email = input.Email?.Trim().ToLowerInvariant();
        Guid? replaced = null;
        if (!string.IsNullOrEmpty(email))
        {
            var user = await db.Users.FirstOrDefaultAsync(u => u.Email == email && u.DeletedAt == null, ct);
            if (user is null)
            {
                return Result.Failure<TeamMemberDto>(UnknownAccount);
            }

            if (user.Status != UserStatus.Active || user.Roles.Any(role => role is AppRoles.PartnerOwner or AppRoles.Admin or AppRoles.SuperAdmin))
            {
                return Result.Failure<TeamMemberDto>(NotEligible);
            }

            replaced = resource.UserId is { } old && old != user.Id ? old : null;
            resource.UserId = user.Id;
            if (!user.Roles.Contains(AppRoles.PartnerStaff))
            {
                user.SetRoles(user.Roles.Append(AppRoles.PartnerStaff));
            }
        }

        await db.SaveChangesAsync(ct);
        if (replaced is { } previous)
        {
            await DropStaffRoleIfUnusedAsync(previous, ct);
        }

        return Result.Success((await ListAsync(ownerId, establishmentId, ct)).Value.First(m => m.ResourceId == resourceId));
    }

    public async Task<Result> UnassignAsync(Guid ownerId, Guid establishmentId, Guid resourceId, CancellationToken ct)
    {
        if (!await Owns(ownerId, establishmentId, ct))
        {
            return Result.Failure(PartnerErrors.NotFound);
        }

        var resource = await db.Resources.FirstOrDefaultAsync(r => r.Id == resourceId && r.EstablishmentId == establishmentId, ct);
        if (resource is null)
        {
            return Result.Failure(MemberNotFound);
        }

        var previous = resource.UserId;
        resource.UserId = null;
        await db.SaveChangesAsync(ct);
        if (previous is { } userId)
        {
            await DropStaffRoleIfUnusedAsync(userId, ct);
        }

        return Result.Success();
    }

    // ───────── Collaborator portal ─────────

    public async Task<IReadOnlyList<TeamMembershipDto>> MembershipsAsync(Guid userId, CancellationToken ct) =>
        await (from r in db.Resources.AsNoTracking()
               join e in db.Establishments.AsNoTracking() on r.EstablishmentId equals e.Id
               where r.UserId == userId && r.IsActive
               orderby e.Name, r.Name
               select new TeamMembershipDto(r.Id, r.Name, e.Id, e.Name, r.CommissionPercent)).ToListAsync(ct);

    /// <summary>The collaborator's appointments of one local day, across every establishment she works for.</summary>
    public async Task<Result<TeamPlanningDto>> PlanningAsync(Guid userId, DateOnly? date, CancellationToken ct)
    {
        var mine = await db.Resources.AsNoTracking().Where(r => r.UserId == userId && r.IsActive).ToListAsync(ct);
        if (mine.Count == 0)
        {
            return Result.Failure<TeamPlanningDto>(MemberNotFound);
        }

        var establishmentIds = mine.Select(r => r.EstablishmentId).Distinct().ToList();
        var establishments = await db.Establishments.AsNoTracking().Where(e => establishmentIds.Contains(e.Id))
            .ToDictionaryAsync(e => e.Id, e => new { e.Name, e.Timezone }, ct);
        var timezone = establishments.Values.First().Timezone;
        var now = time.GetUtcNow();
        var day = date ?? LocalDay(now, timezone);

        // Each establishment has its own local day: fetch a 3-day UTC band and keep what falls on that day locally.
        var around = new DateTimeOffset(day.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero);
        var bandFrom = around.AddDays(-1);
        var bandTo = around.AddDays(2);
        var resourceIds = mine.Select(r => r.Id).ToList();
        var candidates = await db.Appointments.AsNoTracking()
            .Where(a => resourceIds.Contains(a.ResourceId) && !a.AwaitingPayment && a.StartsAt >= bandFrom && a.StartsAt < bandTo
                && (a.Status == AppointmentStatus.Confirmed || a.Status == AppointmentStatus.Completed || a.Status == AppointmentStatus.NoShow))
            .OrderBy(a => a.StartsAt).ToListAsync(ct);
        var appointments = candidates.Where(a => LocalDay(a.StartsAt, establishments[a.EstablishmentId].Timezone) == day).ToList();

        var ids = appointments.Select(a => a.Id).ToList();
        var lines = (await db.AppointmentLines.AsNoTracking().Where(l => ids.Contains(l.AppointmentId)).ToListAsync(ct))
            .GroupBy(l => l.AppointmentId)
            .ToDictionary(g => g.Key, g => (IReadOnlyList<AppointmentLineDto>)g.Select(l => new AppointmentLineDto(l.NameSnapshot, l.PriceSnapshot, l.DurationSnapshot)).ToList());
        var clientIds = appointments.Where(a => a.ClientUserId != null).Select(a => a.ClientUserId!.Value).Distinct().ToList();
        var names = await db.Users.AsNoTracking().Where(u => clientIds.Contains(u.Id))
            .ToDictionaryAsync(u => u.Id, u => $"{u.FirstName} {u.LastName}".Trim(), ct);
        var notes = await db.ClientTechnicalNotes.AsNoTracking()
            .Where(n => establishmentIds.Contains(n.EstablishmentId) && clientIds.Contains(n.ClientUserId))
            .ToDictionaryAsync(n => (n.EstablishmentId, n.ClientUserId), n => n.Allergies, ct);
        var resourceNames = mine.ToDictionary(r => r.Id, r => r.Name);

        var items = appointments.Select(a => new TeamAppointmentDto(
            a.Id,
            a.EstablishmentId,
            establishments[a.EstablishmentId].Name,
            resourceNames[a.ResourceId],
            ClientName(a, names),
            a.ClientUserId,
            a.StartsAt,
            a.EndsAt,
            a.Status.ToString(),
            lines.GetValueOrDefault(a.Id) ?? [],
            a.StartedAt,
            a.FinishedAt,
            a.TipAmount,
            a.ClientUserId is { } c ? notes.GetValueOrDefault((a.EstablishmentId, c)) : null,
            CanStart(a, now),
            a.StartedAt != null && a.FinishedAt == null)).ToList();
        return Result.Success(new TeamPlanningDto(day.ToString("yyyy-MM-dd"), timezone, items));
    }

    public async Task<Result<TeamAppointmentDto>> StartAsync(Guid userId, Guid appointmentId, CancellationToken ct)
    {
        var appointment = await MineAsync(userId, appointmentId, ct);
        if (appointment is null)
        {
            return Result.Failure<TeamAppointmentDto>(SchedulingErrors.AppointmentNotFound);
        }

        if (!CanStart(appointment, time.GetUtcNow()))
        {
            return Result.Failure<TeamAppointmentDto>(SchedulingErrors.InvalidTransition);
        }

        var started = time.GetUtcNow();
        var changed = await db.Appointments.Where(a => a.Id == appointmentId && a.StartedAt == null)
            .ExecuteUpdateAsync(set => set.SetProperty(a => a.StartedAt, started), ct);
        if (changed == 0)
        {
            return Result.Failure<TeamAppointmentDto>(SchedulingErrors.InvalidTransition);
        }

        appointment.StartedAt = started;
        return await ProjectAsync(userId, appointment, ct);
    }

    public async Task<Result<TeamAppointmentDto>> FinishAsync(Guid userId, Guid appointmentId, CancellationToken ct)
    {
        var appointment = await MineAsync(userId, appointmentId, ct);
        if (appointment is null)
        {
            return Result.Failure<TeamAppointmentDto>(SchedulingErrors.AppointmentNotFound);
        }

        var finished = time.GetUtcNow();
        if (appointment.StartedAt is not { } startedAt || appointment.FinishedAt is not null || finished - startedAt < MinDuration)
        {
            return Result.Failure<TeamAppointmentDto>(SchedulingErrors.InvalidTransition);
        }

        var changed = await db.Appointments.Where(a => a.Id == appointmentId && a.FinishedAt == null)
            .ExecuteUpdateAsync(set => set.SetProperty(a => a.FinishedAt, finished), ct);
        if (changed == 0)
        {
            return Result.Failure<TeamAppointmentDto>(SchedulingErrors.InvalidTransition);
        }

        appointment.FinishedAt = finished;
        return await ProjectAsync(userId, appointment, ct);
    }

    // ───────── Technical notes (allergies, what to remember) ─────────

    public async Task<Result<TechnicalNoteDto>> GetNoteAsync(Guid userId, Guid establishmentId, Guid clientUserId, CancellationToken ct)
    {
        var allowed = await CanReadClientAsync(userId, establishmentId, clientUserId, ct);
        if (allowed.IsFailure)
        {
            return Result.Failure<TechnicalNoteDto>(allowed.Error);
        }

        var note = await db.ClientTechnicalNotes.AsNoTracking().FirstOrDefaultAsync(n => n.EstablishmentId == establishmentId && n.ClientUserId == clientUserId, ct);
        return Result.Success(new TechnicalNoteDto(note?.Allergies, note?.Notes, note?.UpdatedAt));
    }

    public async Task<Result<TechnicalNoteDto>> SaveNoteAsync(Guid userId, Guid establishmentId, Guid clientUserId, TechnicalNoteInput input, CancellationToken ct)
    {
        var allowed = await CanReadClientAsync(userId, establishmentId, clientUserId, ct);
        if (allowed.IsFailure)
        {
            return Result.Failure<TechnicalNoteDto>(allowed.Error);
        }

        var allergies = Clean(input.Allergies);
        var notes = Clean(input.Notes);
        if (allergies?.Length > MaxAllergiesLength || notes?.Length > MaxNoteLength)
        {
            return Result.Failure<TechnicalNoteDto>(SchedulingErrors.Invalid($"Allergies : {MaxAllergiesLength} caractères au plus ; notes : {MaxNoteLength}."));
        }

        var note = await db.ClientTechnicalNotes.FirstOrDefaultAsync(n => n.EstablishmentId == establishmentId && n.ClientUserId == clientUserId, ct);
        if (note is null)
        {
            note = new ClientTechnicalNote { EstablishmentId = establishmentId, ClientUserId = clientUserId };
            db.ClientTechnicalNotes.Add(note);
        }

        note.Allergies = allergies;
        note.Notes = notes;
        note.UpdatedByUserId = userId;
        note.UpdatedAt = time.GetUtcNow();
        await db.SaveChangesAsync(ct);
        return Result.Success(new TechnicalNoteDto(note.Allergies, note.Notes, note.UpdatedAt));
    }

    // ───────── Helpers ─────────

    public static bool CanStart(Appointment a, DateTimeOffset now) =>
        a.Status == AppointmentStatus.Confirmed && !a.AwaitingPayment && a.StartedAt == null
        && a.StartsAt - now <= EarlyStart && now - a.EndsAt <= LateStart;

    private static DateOnly LocalDay(DateTimeOffset instant, string timezone) =>
        DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(instant, AppointmentWorkflow.ZoneOf(timezone)).DateTime);

    private static string? Clean(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static string ClientName(Appointment a, IReadOnlyDictionary<Guid, string> names) =>
        a.WalkInName ?? (a.ClientUserId is { } id && names.TryGetValue(id, out var name) ? name : "Cliente");

    private Task<bool> Owns(Guid ownerId, Guid establishmentId, CancellationToken ct) =>
        PartnerSupport.OwnedBy(db, ownerId, establishmentId).AnyAsync(ct);

    private Task<Appointment?> MineAsync(Guid userId, Guid appointmentId, CancellationToken ct) =>
        (from a in db.Appointments
         join r in db.Resources on a.ResourceId equals r.Id
         where a.Id == appointmentId && r.UserId == userId && r.IsActive
         select a).FirstOrDefaultAsync(ct);

    /// <summary>Re-reads the planning of the appointment local day so the answer matches what the portal shows.</summary>
    private async Task<Result<TeamAppointmentDto>> ProjectAsync(Guid userId, Appointment appointment, CancellationToken ct)
    {
        var timezone = await db.Establishments.AsNoTracking().Where(e => e.Id == appointment.EstablishmentId).Select(e => e.Timezone).FirstAsync(ct);
        var planning = await PlanningAsync(userId, LocalDay(appointment.StartsAt, timezone), ct);
        var found = planning.IsSuccess ? planning.Value.Appointments.FirstOrDefault(a => a.Id == appointment.Id) : null;
        return found is null ? Result.Failure<TeamAppointmentDto>(SchedulingErrors.AppointmentNotFound) : Result.Success(found);
    }

    /// <summary>The owner or a collaborator of the establishment may read a client's notes, only if the client has booked there.</summary>
    private async Task<Result> CanReadClientAsync(Guid userId, Guid establishmentId, Guid clientUserId, CancellationToken ct)
    {
        var isMember = await db.Resources.AnyAsync(r => r.EstablishmentId == establishmentId && r.UserId == userId && r.IsActive, ct)
            || await Owns(userId, establishmentId, ct);
        if (!isMember)
        {
            return Result.Failure(PartnerErrors.NotFound);
        }

        return await db.Appointments.AnyAsync(a => a.EstablishmentId == establishmentId && a.ClientUserId == clientUserId, ct)
            ? Result.Success()
            : Result.Failure(ClientNotFound);
    }

    private Task DropStaffRoleIfUnusedAsync(Guid userId, CancellationToken ct) => DropStaffRoleIfUnusedAsync(db, userId, ct);

    /// <summary>Takes the staff role back from an account no resource points to any more.</summary>
    public static async Task DropStaffRoleIfUnusedAsync(KekeBeautyDbContext db, Guid userId, CancellationToken ct)
    {
        if (await db.Resources.AnyAsync(r => r.UserId == userId, ct))
        {
            return;
        }

        var user = await db.Users.FirstOrDefaultAsync(u => u.Id == userId, ct);
        if (user is not null && user.Roles.Contains(AppRoles.PartnerStaff))
        {
            user.SetRoles(user.Roles.Where(role => role != AppRoles.PartnerStaff));
            await db.SaveChangesAsync(ct);
        }
    }
}
