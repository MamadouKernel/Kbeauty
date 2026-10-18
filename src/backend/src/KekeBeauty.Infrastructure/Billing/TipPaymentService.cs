using KekeBeauty.Infrastructure.Admin;
using KekeBeauty.Infrastructure.Persistence;
using KekeBeauty.Infrastructure.Scheduling;
using KekeBeauty.Modules.Billing.Domain;
using KekeBeauty.SharedKernel;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace KekeBeauty.Infrastructure.Billing;

/// <summary>What happened to a tip request: recorded straight away, or waiting for the client to pay on the payment page.</summary>
public sealed record TipResultDto(decimal Amount, string Status, string? PaymentUrl);

/// <summary>
/// Tips paid online. The client pays the platform through the usual payment page; the amount is then added to the appointment and
/// stays on the platform until it is paid out to the collaborator (<see cref="CollaboratorPayoutService"/>).
/// When online payments are switched off on the platform, a tip is only recorded and handed over directly.
/// </summary>
public sealed class TipPaymentService(
    KekeBeautyDbContext db,
    IPaymentGateway gateway,
    IOptions<PaymentsOptions> payments,
    AppSettingsService settings,
    CommissionService commissions,
    NotificationService notifications,
    TimeProvider time,
    ILogger<TipPaymentService> logger)
{
    public async Task<Result<TipResultDto>> TipAsync(Guid clientId, Guid appointmentId, decimal amount, CancellationToken ct)
    {
        var allowed = await commissions.CheckTipAsync(clientId, appointmentId, amount, ct);
        if (allowed.IsFailure)
        {
            return Result.Failure<TipResultDto>(allowed.Error);
        }

        if (!(await settings.GetAsync(ct)).AppointmentPaymentsEnabled)
        {
            var recorded = await commissions.TipAsync(clientId, appointmentId, amount, ct);
            return recorded.IsSuccess
                ? Result.Success(new TipResultDto(amount, "Recorded", null))
                : Result.Failure<TipResultDto>(recorded.Error);
        }

        if (await db.TipPayments.AnyAsync(t => t.AppointmentId == appointmentId && t.Status == TipPaymentStatus.Paid, ct))
        {
            return Result.Failure<TipResultDto>(CommissionService.AlreadyTipped);
        }

        // A page is already open for this appointment: go back to it rather than opening a second one.
        var open = await db.TipPayments.AsNoTracking()
            .Where(t => t.AppointmentId == appointmentId && t.Status == TipPaymentStatus.Awaiting && t.PaymentUrl != null)
            .OrderByDescending(t => t.CreatedAt).FirstOrDefaultAsync(ct);
        if (open is not null)
        {
            return Result.Success(new TipResultDto(open.Amount, "Awaiting", open.PaymentUrl));
        }

        var appointment = allowed.Value;
        var collaborator = await db.Resources.AsNoTracking().Where(r => r.Id == appointment.ResourceId).Select(r => r.UserId).FirstAsync(ct);
        if (collaborator is not { } collaboratorId)
        {
            return Result.Failure<TipResultDto>(CommissionService.NotTippable);
        }

        var tip = new TipPayment
        {
            AppointmentId = appointment.Id,
            EstablishmentId = appointment.EstablishmentId,
            ClientUserId = clientId,
            CollaboratorUserId = collaboratorId,
            Amount = amount,
            CurrencyCode = appointment.CurrencyCode,
            CreatedAt = time.GetUtcNow(),
        };
        var transactionId = "KT" + Guid.NewGuid().ToString("N")[..20].ToUpperInvariant();
        var payment = new Payment
        {
            TipPaymentId = tip.Id,
            Provider = gateway.Provider,
            ProviderTransactionId = transactionId,
            Amount = amount,
            CurrencyCode = tip.CurrencyCode,
            CreatedAt = tip.CreatedAt,
        };
        tip.PaymentId = payment.Id;
        db.TipPayments.Add(tip);
        db.Payments.Add(payment);
        await db.SaveChangesAsync(ct);

        var client = await db.Users.AsNoTracking().Where(u => u.Id == clientId).Select(u => new { u.FirstName, u.LastName, u.Email }).FirstAsync(ct);
        var establishment = await db.Establishments.AsNoTracking().Where(e => e.Id == appointment.EstablishmentId).Select(e => e.Name).FirstAsync(ct);
        var urls = payments.Value;
        try
        {
            var session = await gateway.CreateAsync(
                new PaymentRequest(
                    transactionId,
                    amount,
                    tip.CurrencyCode,
                    $"Pourboire — {establishment}",
                    $"{client.FirstName} {client.LastName}".Trim(),
                    client.Email,
                    appointment.ClientPhone ?? string.Empty,
                    $"{urls.WebBaseUrl.TrimEnd('/')}/compte/rendez-vous?pourboire={transactionId}",
                    $"{urls.ApiBaseUrl.TrimEnd('/')}/api/v1/webhooks/payments/{gateway.Provider}"),
                ct);
            payment.ProviderReference = session.ProviderReference;
            tip.PaymentUrl = session.PaymentUrl;
            await db.SaveChangesAsync(ct);
            return Result.Success(new TipResultDto(amount, "Awaiting", tip.PaymentUrl));
        }
        catch (PaymentGatewayException ex)
        {
            logger.LogWarning(ex, "The tip payment page of appointment {Appointment} could not be opened", appointment.Id);
            payment.Status = PaymentStatus.Failed;
            payment.RawPayload = "gateway unavailable at creation";
            tip.Status = TipPaymentStatus.Failed;
            await db.SaveChangesAsync(ct);
            return Result.Failure<TipResultDto>(BillingErrors.GatewayUnavailable);
        }
    }

    /// <summary>Called by the payment processor once the provider's verdict on a tip is known (already claimed, so once only).</summary>
    public async Task OnSettledAsync(Payment payment, bool succeeded, CancellationToken ct)
    {
        var tip = payment.TipPaymentId is { } id ? await db.TipPayments.FirstOrDefaultAsync(t => t.Id == id, ct) : null;
        if (tip is null || tip.Status != TipPaymentStatus.Awaiting)
        {
            return;
        }

        if (!succeeded)
        {
            tip.Status = TipPaymentStatus.Failed;
            await db.SaveChangesAsync(ct);
            return;
        }

        var amount = tip.Amount;
        tip.Status = TipPaymentStatus.Paid;
        tip.PaidAt = time.GetUtcNow();
        await db.SaveChangesAsync(ct);
        await db.Appointments.Where(a => a.Id == tip.AppointmentId)
            .ExecuteUpdateAsync(set => set.SetProperty(a => a.TipAmount, a => a.TipAmount + amount), ct);
        await notifications.NotifyAsync(tip.CollaboratorUserId, "tip.received", "Pourboire reçu",
            $"Une cliente vous a laissé un pourboire de {amount:0} F CFA. Il vous sera versé avec vos prochains gains.", null, ct);
    }
}
