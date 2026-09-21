using KekeBeauty.Application.Billing;
using KekeBeauty.Application.Rdv;

namespace KekeBeauty.Api.BackgroundJobs;

public sealed class WavePayoutBackgroundService : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<WavePayoutBackgroundService> _logger;
    public WavePayoutBackgroundService(IServiceScopeFactory scopeFactory, ILogger<WavePayoutBackgroundService> logger)
    { _scopeFactory = scopeFactory; _logger = logger; }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromMinutes(1));
        do
        {
            try
            {
                using var scope = _scopeFactory.CreateScope();
                var repository = scope.ServiceProvider.GetRequiredService<IPourboireRepository>();
                var wave = scope.ServiceProvider.GetRequiredService<IWaveMoneyGateway>();
                foreach (var item in await repository.ListerAReverserAsync(stoppingToken))
                {
                    var result = await wave.CreatePayoutAsync(item.Telephone, item.Nom, item.Montant,
                        item.IdPourboire.ToString("N"), item.IdempotencyKey, stoppingToken);
                    var status = result.Success ? result.Status.ToUpperInvariant() : "ECHEC";
                    await repository.EnregistrerReversementAsync(item.IdPourboire, result.Success, status,
                        result.Reference, result.Error, stoppingToken);
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            { _logger.LogError(ex, "Echec du traitement des reversements Wave."); }
        } while (await timer.WaitForNextTickAsync(stoppingToken));
    }
}