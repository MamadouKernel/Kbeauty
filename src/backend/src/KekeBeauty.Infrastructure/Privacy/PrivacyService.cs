using KekeBeauty.Infrastructure.Admin;
using KekeBeauty.Infrastructure.Persistence;
using KekeBeauty.Modules.Catalog.Domain;
using KekeBeauty.Modules.Identity.Domain;
using KekeBeauty.Modules.Scheduling.Domain;
using KekeBeauty.SharedKernel;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace KekeBeauty.Infrastructure.Privacy;

public sealed record ExportedAppointment(Guid Id, string Establishment, DateTimeOffset StartsAt, string Status, string? Note, string? Phone);

public sealed record ExportedEstablishment(Guid Id, string Name, string Status, DateTimeOffset CreatedAt);

public sealed record ExportedNotification(string Title, string Body, DateTimeOffset CreatedAt);

/// <summary>Everything the platform holds about one person (right of access, law no. 2013-450).</summary>
public sealed record PersonalDataExport(
    DateTimeOffset GeneratedAt,
    Guid UserId,
    string Email,
    string FirstName,
    string LastName,
    IReadOnlyList<string> Roles,
    DateTimeOffset CreatedAt,
    DateTimeOffset? TermsAcceptedAt,
    bool TwoFactorEnabled,
    IReadOnlyList<ExportedEstablishment> Establishments,
    IReadOnlyList<ExportedAppointment> Appointments,
    IReadOnlyList<ExportedNotification> Notifications);

public sealed record DeleteAccountInput(string? Password);

public static class PrivacyErrors
{
    public static readonly Error NotFound = new("user.not_found", "Compte introuvable.");
    public static readonly Error WrongPassword = new("account.wrong_password", "Mot de passe incorrect.");
    public static readonly Error HasEstablishments = new("account.has_establishments", "Votre compte gère des établissements publiés ou en cours de validation : contactez l'équipe Keke Beauty pour les clôturer avant de supprimer le compte.");
    public static readonly Error HasUpcomingAppointments = new("account.has_upcoming_appointments", "Vous avez des rendez-vous à venir : annulez-les avant de supprimer votre compte.");
}

/// <summary>Right of access (export) and right to erasure (anonymisation) for the signed-in user.</summary>
public sealed class PrivacyService(KekeBeautyDbContext db, IPasswordHasher<User> hasher, IFileStorage storage, TimeProvider time)
{
    private static readonly AppointmentStatus[] Upcoming =
        [AppointmentStatus.Pending, AppointmentStatus.Confirmed, AppointmentStatus.RescheduleProposed];

    private static readonly EstablishmentStatus[] KeptStatuses =
        [EstablishmentStatus.Active, EstablishmentStatus.PendingReview, EstablishmentStatus.Suspended];

    public async Task<Result<PersonalDataExport>> ExportAsync(Guid userId, string? ip, CancellationToken ct)
    {
        var user = await db.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Id == userId && u.DeletedAt == null, ct);
        if (user is null)
        {
            return Result.Failure<PersonalDataExport>(PrivacyErrors.NotFound);
        }

        var establishments = await db.Establishments.AsNoTracking().Where(e => e.OwnerUserId == userId)
            .OrderBy(e => e.CreatedAt).Select(e => new ExportedEstablishment(e.Id, e.Name, e.Status.ToString(), e.CreatedAt)).ToListAsync(ct);
        var appointments = await (
            from a in db.Appointments.AsNoTracking()
            where a.ClientUserId == userId
            join e in db.Establishments.AsNoTracking() on a.EstablishmentId equals e.Id
            orderby a.StartsAt
            select new ExportedAppointment(a.Id, e.Name, a.StartsAt, a.Status.ToString(), a.ClientNote, a.ClientPhone))
            .ToListAsync(ct);
        var notifications = await db.Notifications.AsNoTracking().Where(n => n.UserId == userId)
            .OrderByDescending(n => n.CreatedAt).Take(500).Select(n => new ExportedNotification(n.Title, n.Body, n.CreatedAt)).ToListAsync(ct);

        db.AuditLogs.Add(new Modules.Administration.Domain.AuditLog
        {
            ActorUserId = userId,
            Action = "privacy.export",
            EntityType = "User",
            EntityId = userId,
            Ip = ip,
            At = time.GetUtcNow(),
        });
        await db.SaveChangesAsync(ct);

        return Result.Success(new PersonalDataExport(
            time.GetUtcNow(), user.Id, user.Email, user.FirstName, user.LastName, user.Roles, user.CreatedAt, user.TermsAcceptedAt,
            user.TotpEnabled, establishments, appointments, notifications));
    }

    public async Task<Result> DeleteAsync(Guid userId, string? password, string? ip, CancellationToken ct)
    {
        var user = await db.Users.FirstOrDefaultAsync(u => u.Id == userId && u.DeletedAt == null, ct);
        if (user is null)
        {
            return Result.Failure(PrivacyErrors.NotFound);
        }

        // Signing in with Google leaves no password: the signed-in session and its 2FA proof are what we rely on.
        if (user.PasswordHash is not null
            && hasher.VerifyHashedPassword(user, user.PasswordHash, password ?? string.Empty) == PasswordVerificationResult.Failed)
        {
            return Result.Failure(PrivacyErrors.WrongPassword);
        }

        if (user.Roles.Contains(AppRoles.Admin) || user.Roles.Contains(AppRoles.SuperAdmin))
        {
            return Result.Failure(AdminErrors.Forbidden);
        }

        var now = time.GetUtcNow();
        if (await db.Establishments.AnyAsync(e => e.OwnerUserId == userId && KeptStatuses.Contains(e.Status), ct))
        {
            return Result.Failure(PrivacyErrors.HasEstablishments);
        }

        if (await db.Appointments.AnyAsync(a => a.ClientUserId == userId && Upcoming.Contains(a.Status) && a.StartsAt > now, ct))
        {
            return Result.Failure(PrivacyErrors.HasUpcomingAppointments);
        }

        var identity = await db.KycIdentities.FirstOrDefaultAsync(k => k.UserId == userId, ct);
        var keys = identity is null ? [] : new[] { identity.FrontKey, identity.BackKey, identity.SelfieKey }.Where(k => k is not null).Select(k => k!).ToList();
        if (identity is not null)
        {
            identity.HolderName = string.Empty;
            identity.DocumentNumber = string.Empty;
            identity.DocumentExpiry = null;
            identity.FrontKey = identity.BackKey = identity.SelfieKey = null;
        }

        // Unpublished drafts stay as hidden shells without an owner; past appointments keep their history without personal details.
        await db.Establishments.Where(e => e.OwnerUserId == userId)
            .ExecuteUpdateAsync(set => set.SetProperty(e => e.OwnerUserId, (Guid?)null), ct);
        await db.Appointments.Where(a => a.ClientUserId == userId)
            .ExecuteUpdateAsync(set => set.SetProperty(a => a.ClientNote, (string?)null).SetProperty(a => a.ClientPhone, (string?)null), ct);
        await db.Favorites.Where(f => f.UserId == userId).ExecuteDeleteAsync(ct); // reviews stay, under the author name "Ancien client"
        await db.Notifications.Where(n => n.UserId == userId).ExecuteDeleteAsync(ct);
        await db.RefreshTokens.Where(t => t.UserId == userId).ExecuteDeleteAsync(ct);
        await db.LoginChallenges.Where(c => c.UserId == userId).ExecuteDeleteAsync(ct);
        await db.RecoveryCodes.Where(c => c.UserId == userId).ExecuteDeleteAsync(ct);
        await db.EmailTokens.Where(t => t.UserId == userId).ExecuteDeleteAsync(ct);

        user.Anonymize(now);
        db.AuditLogs.Add(new Modules.Administration.Domain.AuditLog
        {
            ActorUserId = userId,
            Action = "privacy.delete",
            EntityType = "User",
            EntityId = userId,
            Ip = ip,
            At = now,
        });
        await db.SaveChangesAsync(ct);

        foreach (var key in keys)
        {
            try
            {
                await storage.DeleteAsync(StorageBuckets.PrivateKyc, key, ct);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                // The database no longer references the file; an orphaned private object is purged with the bucket lifecycle rules.
            }
        }

        return Result.Success();
    }
}
