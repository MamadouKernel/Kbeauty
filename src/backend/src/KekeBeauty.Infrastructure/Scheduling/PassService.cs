using KekeBeauty.Infrastructure.Persistence;
using KekeBeauty.Modules.Scheduling.Domain;
using KekeBeauty.SharedKernel;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;

namespace KekeBeauty.Infrastructure.Scheduling;

public sealed record PassDto(string Token, DateTimeOffset ExpiresAt);

/// <summary>What the establishment sees after scanning a client's pass.</summary>
public sealed record PassCheckDto(
    Guid AppointmentId,
    string ClientName,
    string? ClientPhone,
    DateTimeOffset StartsAt,
    DateTimeOffset EndsAt,
    string Status,
    decimal TotalPrice,
    string CurrencyCode,
    string? ResourceName,
    IReadOnlyList<AppointmentLineDto> Lines,
    bool IsToday,
    bool IsPaid);

public sealed record PassVerifyInput(string Token);

/// <summary>
/// The pass is a tamper-proof, time-limited token the client shows (as a QR code) at the counter. It carries only the
/// appointment id; the establishment checks it belongs to them, so a pass for another salon is refused.
/// </summary>
public sealed class PassService(KekeBeautyDbContext db, IDataProtectionProvider protection, TimeProvider time)
{
    /// <summary>Valid from creation until this long after the appointment ends.</summary>
    public static readonly TimeSpan GraceAfterEnd = TimeSpan.FromHours(6);

    public static readonly Error InvalidPass = new("pass.invalid", "Ce pass est invalide, altéré ou expiré.");
    public static readonly Error ForeignPass = new("pass.foreign", "Ce pass appartient à un autre établissement.");

    private ITimeLimitedDataProtector Protector => protection.CreateProtector("KekeBeauty.Scheduling.Pass.v1").ToTimeLimitedDataProtector();

    public async Task<Result<PassDto>> IssueAsync(Guid clientId, Guid appointmentId, CancellationToken ct)
    {
        var appointment = await db.Appointments.AsNoTracking().FirstOrDefaultAsync(a => a.Id == appointmentId && a.ClientUserId == clientId, ct);
        if (appointment is null)
        {
            return Result.Failure<PassDto>(SchedulingErrors.AppointmentNotFound);
        }

        if (appointment.Status is not (AppointmentStatus.Confirmed or AppointmentStatus.Completed) || appointment.AwaitingPayment)
        {
            return Result.Failure<PassDto>(SchedulingErrors.Invalid("Le pass est disponible dès que le rendez-vous est confirmé."));
        }

        var expires = appointment.EndsAt + GraceAfterEnd;
        if (expires <= time.GetUtcNow())
        {
            return Result.Failure<PassDto>(SchedulingErrors.Invalid("Ce rendez-vous est passé depuis trop longtemps."));
        }

        return Result.Success(new PassDto(Protector.Protect(appointment.Id.ToString("N"), expires), expires));
    }

    public async Task<Result<PassCheckDto>> VerifyAsync(Guid ownerId, Guid establishmentId, string token, CancellationToken ct)
    {
        if (!await db.Establishments.AnyAsync(e => e.Id == establishmentId && e.OwnerUserId == ownerId, ct))
        {
            return Result.Failure<PassCheckDto>(KekeBeauty.Infrastructure.Partner.PartnerErrors.NotFound);
        }

        Guid id;
        try
        {
            // Tolerate a pasted link: the token is whatever follows "token=".
            var raw = token.Trim();
            var marker = raw.IndexOf("token=", StringComparison.Ordinal);
            if (marker >= 0)
            {
                raw = Uri.UnescapeDataString(raw[(marker + 6)..]);
            }

            if (!Guid.TryParseExact(Protector.Unprotect(raw), "N", out id))
            {
                return Result.Failure<PassCheckDto>(InvalidPass);
            }
        }
        catch (System.Security.Cryptography.CryptographicException)
        {
            return Result.Failure<PassCheckDto>(InvalidPass);
        }

        var appointment = await db.Appointments.AsNoTracking().FirstOrDefaultAsync(a => a.Id == id, ct);
        if (appointment is null)
        {
            return Result.Failure<PassCheckDto>(InvalidPass);
        }

        if (appointment.EstablishmentId != establishmentId)
        {
            return Result.Failure<PassCheckDto>(ForeignPass);
        }

        var client = appointment.ClientUserId is { } clientId
            ? await db.Users.AsNoTracking().Where(u => u.Id == clientId).Select(u => u.FirstName + " " + u.LastName).FirstOrDefaultAsync(ct)
            : null;
        var resource = await db.Resources.AsNoTracking().Where(r => r.Id == appointment.ResourceId).Select(r => r.Name).FirstOrDefaultAsync(ct);
        var lines = await db.AppointmentLines.AsNoTracking().Where(l => l.AppointmentId == id)
            .Select(l => new AppointmentLineDto(l.NameSnapshot, l.PriceSnapshot, l.DurationSnapshot)).ToListAsync(ct);
        var zoneId = await db.Establishments.AsNoTracking().Where(e => e.Id == establishmentId).Select(e => e.Timezone).FirstAsync(ct);
        var zone = AppointmentWorkflow.ZoneOf(zoneId);
        var now = time.GetUtcNow();
        var isToday = TimeZoneInfo.ConvertTime(appointment.StartsAt, zone).Date == TimeZoneInfo.ConvertTime(now, zone).Date;

        return Result.Success(new PassCheckDto(
            appointment.Id, appointment.WalkInName ?? client?.Trim() ?? "Client", appointment.ClientPhone,
            appointment.StartsAt, appointment.EndsAt, appointment.Status.ToString(),
            appointment.TotalPrice, appointment.CurrencyCode, resource, lines, isToday, appointment.ServicePaidAt is not null));
    }
}
