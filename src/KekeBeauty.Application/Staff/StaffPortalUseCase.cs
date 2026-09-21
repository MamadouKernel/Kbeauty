namespace KekeBeauty.Application.Staff;

/// <summary>Regroupe les 3 operations du portail collaboratrice derriere un seul use case (meme
/// justification de consolidation que IStaffRepository).</summary>
public sealed class StaffPortalUseCase
{
    private readonly IStaffRepository _repository;

    public StaffPortalUseCase(IStaffRepository repository)
    {
        _repository = repository;
    }

    public Task<IReadOnlyList<PlanningItem>> GetPlanningAsync(Guid idCollaborateur, CancellationToken cancellationToken) =>
        _repository.GetPlanningAsync(idCollaborateur, cancellationToken);

    public Task<IReadOnlyList<CommissionItem>> GetCommissionsAsync(Guid idCollaborateur, CancellationToken cancellationToken) =>
        _repository.GetCommissionsAsync(idCollaborateur, cancellationToken);

    public Task<string?> GetFicheTechniqueAsync(Guid idRdv, Guid idCollaborateur, CancellationToken cancellationToken) =>
        _repository.GetFicheTechniqueAsync(idRdv, idCollaborateur, cancellationToken);

    public Task<bool> EnregistrerFicheTechniqueAsync(Guid idRdv, Guid idCollaborateur, string notes, CancellationToken cancellationToken) =>
        _repository.EnregistrerFicheTechniqueAsync(idRdv, idCollaborateur, notes, cancellationToken);

    public Task<string> UpdateWorkSessionAsync(Guid idRdv, Guid idCollaborateur, string action, CancellationToken cancellationToken) =>
        _repository.UpdateWorkSessionAsync(idRdv, idCollaborateur, action, cancellationToken);}
