namespace KekeBeauty.Application.Onboarding;

public sealed class ValidateApplicationUseCase
{
    private readonly IEtablissementRepository _repository;

    public ValidateApplicationUseCase(IEtablissementRepository repository)
    {
        _repository = repository;
    }

    public async Task<ApplicationDecisionResult> ExecuteAsync(Guid idEtablissement, CancellationToken cancellationToken)
    {
        var application = await _repository.GetByIdAsync(idEtablissement, cancellationToken);
        if (application is null)
        {
            return new ApplicationDecisionResult(false, "not_found");
        }

        if (!application.HasPhotoDevanture || !application.HasPieceIdentite)
        {
            return new ApplicationDecisionResult(false, "missing_documents");
        }

        await _repository.UpdateStatutAsync(idEtablissement, "VALIDE", cancellationToken);
        return new ApplicationDecisionResult(true, "VALIDE");
    }
}
