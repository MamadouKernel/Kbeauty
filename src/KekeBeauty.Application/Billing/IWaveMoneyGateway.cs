namespace KekeBeauty.Application.Billing;

public sealed record WaveOperationResult(bool Success, string Status, string? Reference = null, string? Error = null)
{
    public static WaveOperationResult NotConfigured() => new(false, "wave_not_configured", Error: "La cle API Wave n'est pas configuree.");
}

public interface IWaveMoneyGateway
{
    Task<WaveOperationResult> RefundCheckoutAsync(string checkoutId, CancellationToken cancellationToken);

    Task<WaveOperationResult> CreatePayoutAsync(
        string mobile,
        string name,
        decimal amount,
        string clientReference,
        string idempotencyKey,
        CancellationToken cancellationToken);
}
