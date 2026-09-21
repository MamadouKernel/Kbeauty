namespace KekeBeauty.Application.Rdv;

public sealed class LaisserAvisResult
{
    public bool Success { get; init; }
    public string Status { get; init; } = "";
    public Guid? IdAvis { get; init; }
}

/// <summary>
/// US1 (014) : creation d'un avis, uniquement pour un RDV TERMINE appartenant au client (FR-001/002),
/// avec unicite garantie par IAvisRepository.CreerAsync (FR-003).
/// </summary>
public sealed class LaisserAvisUseCase
{
    private readonly IRdvRepository _rdvRepository;
    private readonly IAvisRepository _avisRepository;

    public LaisserAvisUseCase(IRdvRepository rdvRepository, IAvisRepository avisRepository)
    {
        _rdvRepository = rdvRepository;
        _avisRepository = avisRepository;
    }

    public async Task<LaisserAvisResult> ExecuteAsync(Guid idRdv, Guid idUtilisateurClient, short note, string? commentaire, CancellationToken cancellationToken)
    {
        if (note is < 1 or > 5)
        {
            return new LaisserAvisResult { Success = false, Status = "note_invalide" };
        }

        // Meme reponse (not_found) que le RDV n'existe pas, n'appartienne pas au client, ou ne soit
        // pas TERMINE - pas de fuite d'information (FR-004).
        var rdv = await _rdvRepository.GetStatutAsync(idRdv, idUtilisateurClient, cancellationToken);
        if (rdv is null || rdv.StatutRdv != "TERMINE")
        {
            return new LaisserAvisResult { Success = false, Status = "not_found" };
        }

        var idAvis = await _avisRepository.CreerAsync(idRdv, note, commentaire, cancellationToken);
        if (idAvis is null)
        {
            return new LaisserAvisResult { Success = false, Status = "avis_deja_existant" };
        }

        return new LaisserAvisResult { Success = true, Status = "created", IdAvis = idAvis };
    }
}
