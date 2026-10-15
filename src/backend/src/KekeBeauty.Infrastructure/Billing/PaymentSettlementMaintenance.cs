using KekeBeauty.Infrastructure.Scheduling;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace KekeBeauty.Infrastructure.Billing;

/// <summary>
/// Follows the money that is still in flight: refunds and payouts the operator had not yet confirmed. Runs every ten minutes and is
/// idempotent; <c>Settlements:MaintenanceEnabled=false</c> switches it off.
/// </summary>
public sealed class PaymentSettlementMaintenance(
    IServiceScopeFactory scopes,
    TimeProvider time,
    IConfiguration configuration,
    ILogger<PaymentSettlementMaintenance> logger) : MaintenanceWorker(time, configuration, logger, "Settlements", TimeSpan.FromMinutes(10))
{
    protected override async Task TickAsync(CancellationToken cancellationToken) => await RunOnceAsync(cancellationToken);

    /// <summary>Returns how many refunds and payouts were looked at.</summary>
    public async Task<int> RunOnceAsync(CancellationToken ct)
    {
        await using var scope = scopes.CreateAsyncScope();
        var refunds = await scope.ServiceProvider.GetRequiredService<AppointmentPaymentService>().CheckPendingRefundsAsync(ct);
        var payouts = await scope.ServiceProvider.GetRequiredService<PayoutService>().CheckPendingAsync(ct);
        return refunds + payouts;
    }
}
