namespace KekeBeauty.Application.Rdv;

public interface IRdvNotifier
{
    Task<bool> NotifyDecisionAsync(string telephone, string statutRdv, DateTimeOffset dateHeureDebut, CancellationToken cancellationToken);
}
