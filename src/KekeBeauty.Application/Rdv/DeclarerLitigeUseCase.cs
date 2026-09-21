namespace KekeBeauty.Application.Rdv;

public sealed class DeclarerLitigeResult
{
    public bool Success { get; init; }
    public string Status { get; init; } = string.Empty;
    public Guid? IdLitige { get; init; }
}

/// <summary>Feature 018 (Parcours 6) : client ou gerant peut declarer un litige sur un RDV.</summary>
public sealed class DeclarerLitigeUseCase
{
    private readonly ILitigeRepository _repository;

    public DeclarerLitigeUseCase(ILitigeRepository repository)
    {
        _repository = repository;
    }

    public async Task<DeclarerLitigeResult> ExecuteAsync(Guid idRdv, Guid idUtilisateurDeclarant, string motif, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(motif))
        {
            return new DeclarerLitigeResult { Success = false, Status = "motif_requis" };
        }

        if (!await _repository.EstPartiePrenanteAsync(idRdv, idUtilisateurDeclarant, cancellationToken))
        {
            return new DeclarerLitigeResult { Success = false, Status = "rdv_introuvable" };
        }

        var idLitige = await _repository.DeclarerAsync(idRdv, idUtilisateurDeclarant, motif, cancellationToken);
        return new DeclarerLitigeResult { Success = true, Status = "ouvert", IdLitige = idLitige };
    }
}
