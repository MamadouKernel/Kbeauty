using KekeBeauty.Application.Rdv;

namespace KekeBeauty.Api.BackgroundJobs;

/// <summary>
/// Feature 018 (Parcours 3) : job periodique qui fait passer les RDV CONFIRME expires a TERMINE
/// et declenche les rappels avis/pourboire (in-app + push). Ferme un gap reel decouvert en
/// construisant Parcours 3 : rien d'autre dans le systeme ne faisait jamais cette transition.
/// </summary>
public sealed class RdvCompletionBackgroundService : BackgroundService
{
    private static readonly TimeSpan Interval = TimeSpan.FromMinutes(5);

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<RdvCompletionBackgroundService> _logger;

    public RdvCompletionBackgroundService(IServiceScopeFactory scopeFactory, ILogger<RdvCompletionBackgroundService> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                using var scope = _scopeFactory.CreateScope();
                var useCase = scope.ServiceProvider.GetRequiredService<MarquerRdvsTerminesUseCase>();
                var count = await useCase.ExecuteAsync(stoppingToken);
                var reminderUseCase = scope.ServiceProvider.GetRequiredService<EnvoyerRappelsRdvUseCase>();
                var reminders = await reminderUseCase.ExecuteAsync(stoppingToken);
                if (reminders > 0) _logger.LogInformation("{Count} rappel(s) de rendez-vous envoyé(s).", reminders);
                if (count > 0)
                {
                    _logger.LogInformation("{Count} RDV passe(s) a TERMINE, rappels avis/pourboire envoyes.", count);
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogWarning(ex, "Echec du job de completion des RDV (nouvelle tentative dans {Interval}).", Interval);
            }

            try
            {
                await Task.Delay(Interval, stoppingToken);
            }
            catch (OperationCanceledException)
            {
                // Arret normal du service.
            }
        }
    }
}

