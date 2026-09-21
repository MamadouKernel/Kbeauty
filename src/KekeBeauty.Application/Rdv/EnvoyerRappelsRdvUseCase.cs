using KekeBeauty.Application.Notifications;
namespace KekeBeauty.Application.Rdv;
public sealed class EnvoyerRappelsRdvUseCase
{
    private readonly IRdvRepository _rdvs; private readonly ClientNotificationService _delivery;
    public EnvoyerRappelsRdvUseCase(IRdvRepository rdvs,ClientNotificationService delivery){_rdvs=rdvs;_delivery=delivery;}
    public async Task<int> ExecuteAsync(CancellationToken ct){var items=await _rdvs.ListerRappelsAsync(ct);foreach(var x in items)await _delivery.SendAsync(x.IdUtilisateurClient,"Rappel de rendez-vous",$"Votre rendez-vous chez {x.NomEtablissement} est prévu demain à {x.DateHeureDebut:HH:mm}.",x.IdRdv,$"/mes-rendez-vous/{x.IdRdv}",ct);return items.Count;}
}
