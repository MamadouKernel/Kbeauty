namespace KekeBeauty.Application.Rdv;

public sealed class RequestRdvUseCase
{
    private readonly IRdvRepository _repository;

    public RequestRdvUseCase(IRdvRepository repository)
    {
        _repository = repository;
    }

    public Task<IReadOnlyList<CreneauOccupe>> GetCreneauxOccupesAsync(Guid idEtablissement, DateOnly date, CancellationToken cancellationToken) =>
        _repository.GetCreneauxOccupesAsync(idEtablissement, date, cancellationToken);

    public async Task<RequestRdvResult> ExecuteAsync(
        Guid idEtablissement, Guid idPrestation, Guid idUtilisateurClient, DateTimeOffset dateHeureDebut, CancellationToken cancellationToken)
    {
        var idRdv = await _repository.CreateIfNoOverlapAsync(idEtablissement, idPrestation, idUtilisateurClient, dateHeureDebut, cancellationToken);

        if (idRdv is null)
        {
            return new RequestRdvResult(false, "invalid_target");
        }

        if (idRdv == Guid.Empty)
        {
            return new RequestRdvResult(false, "slot_unavailable");
        }

        return new RequestRdvResult(true, "DEMANDE", idRdv);
    }
}
