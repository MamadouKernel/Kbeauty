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

    public Task<int> GetMinimumBookingNoticeMinutesAsync(CancellationToken cancellationToken) =>
        _repository.GetMinimumBookingNoticeMinutesAsync(cancellationToken);

    public async Task<RequestRdvResult> ExecuteAsync(
        Guid idEtablissement, Guid idPrestation, Guid idUtilisateurClient, DateTimeOffset dateHeureDebut, string? modePaiementChoisi, CancellationToken cancellationToken)
    {
        var minimumNotice = await _repository.GetMinimumBookingNoticeMinutesAsync(cancellationToken);
        if (dateHeureDebut < DateTimeOffset.UtcNow.AddMinutes(minimumNotice))
        {
            return new RequestRdvResult(false, "booking_too_soon");
        }

        var idRdv = await _repository.CreateIfNoOverlapAsync(idEtablissement, idPrestation, idUtilisateurClient, dateHeureDebut, modePaiementChoisi, cancellationToken);

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
