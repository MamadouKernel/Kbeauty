using KekeBeauty.Application.Notifications;
namespace KekeBeauty.Application.Rdv;

public sealed class AnnulerRdvResult
{
    public bool Success { get; init; }
    public string Status { get; init; } = "";
    public bool RemboursementEnCours { get; init; }
}

/// <summary>US2 (015), processus revu en 018 : annulation client + creation d'une DEMANDE de
/// remboursement (REMBOURSEMENT_DEMANDE) si un paiement REUSSIE existait (FR-003/004/005). Le
/// remboursement reel reste un traitement manuel par l'administrateur (pas d'API WinPayer de
/// remboursement) - voir GererRemboursementsUseCase pour l'etape de traitement.</summary>
public sealed class AnnulerRdvUseCase
{
    private readonly IRdvRepository _rdvRepository;
    private readonly IRdvPaiementRepository _paiementRepository;
    private readonly ClientNotificationService _notificationService;

    public AnnulerRdvUseCase(IRdvRepository rdvRepository, IRdvPaiementRepository paiementRepository, ClientNotificationService notificationService)
    {
        _rdvRepository = rdvRepository;
        _paiementRepository = paiementRepository;
        _notificationService = notificationService;
    }

    public async Task<AnnulerRdvResult> ExecuteAsync(Guid idRdv, Guid idUtilisateurClient, CancellationToken cancellationToken)
    {
        var dateRdv = await _rdvRepository.GetDateHeureDebutAsync(idRdv, cancellationToken);
        var eligibleKekeProtect = dateRdv is not null && dateRdv.Value - DateTimeOffset.UtcNow >= TimeSpan.FromHours(4);
        var annule = await _rdvRepository.AnnulerParClientAsync(idRdv, idUtilisateurClient, cancellationToken);
        if (!annule)
        {
            return new AnnulerRdvResult { Success = false, Status = "not_found" };
        }

        var rembourse = eligibleKekeProtect && await _paiementRepository.DemanderRembourseAsync(idRdv, cancellationToken);
        await _notificationService.SendAsync(idUtilisateurClient, "Rendez-vous annulé", rembourse ? "Votre rendez-vous est annulé. Kéké Protect couvre votre remboursement intégral." : eligibleKekeProtect ? "Votre rendez-vous est annulé." : "Votre rendez-vous est annulé. Le délai Kéké Protect de 4 heures est dépassé.", idRdv, "/mes-rendez-vous", cancellationToken);

        return new AnnulerRdvResult { Success = true, Status = "annule", RemboursementEnCours = rembourse };
    }
}

