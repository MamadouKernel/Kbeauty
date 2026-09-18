using System.Globalization;

namespace KekeBeauty.Application.Directory;

public sealed class GetEtablissementDetailUseCase
{
    private readonly IDirectoryRepository _repository;

    public GetEtablissementDetailUseCase(IDirectoryRepository repository)
    {
        _repository = repository;
    }

    public async Task<EtablissementDetail?> ExecuteAsync(Guid idEtablissement, CancellationToken cancellationToken)
    {
        // FR-007 : null pour "inexistant" ET pour "non valide" - reponse identique cote controleur
        // (voir research.md, Decision 2).
        var core = await _repository.GetValidatedCoreAsync(idEtablissement, cancellationToken);
        if (core is null)
        {
            return null;
        }

        var medias = await _repository.GetMediasAsync(idEtablissement, cancellationToken);
        var prestations = await _repository.GetPrestationsAsync(idEtablissement, cancellationToken);

        var lienItineraire = string.Create(CultureInfo.InvariantCulture, $"https://www.google.com/maps/dir/?api=1&destination={core.GpsLatitude},{core.GpsLongitude}");

        return new EtablissementDetail(
            core.IdEtablissement,
            core.NomEtablissement,
            core.Description,
            core.NumeroServiceClient,
            lienItineraire,
            medias,
            prestations);
    }
}
