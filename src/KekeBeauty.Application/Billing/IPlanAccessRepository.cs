namespace KekeBeauty.Application.Billing;

public class PlanEntitlements
{
    public string Formule { get; set; } = "FREE";
    public int? LimitePrestations { get; set; }
    public int? LimiteRdvMensuels { get; set; }
    public bool PaiementMobile { get; set; }
    public bool GestionEquipe { get; set; }
    public bool StatistiquesAvancees { get; set; }
}

public sealed class PlanUsage : PlanEntitlements
{
    public int PrestationsUtilisees { get; init; }
    public int RendezVousMoisUtilises { get; init; }
}

public interface IPlanAccessRepository
{
    Task<bool> IsProAsync(Guid idEtablissement, CancellationToken cancellationToken);
    Task<PlanEntitlements?> GetEntitlementsAsync(string formule, CancellationToken cancellationToken);
    Task<IReadOnlyList<PlanEntitlements>> ListEntitlementsAsync(CancellationToken cancellationToken);
    Task<bool> UpdateEntitlementsAsync(PlanEntitlements entitlements, CancellationToken cancellationToken);
    Task<int> CountPrestationsAsync(Guid idEtablissement, CancellationToken cancellationToken);
    Task<int> CountRendezVousCurrentMonthAsync(Guid idEtablissement, CancellationToken cancellationToken);
}

public sealed class PlanAccessService
{
    private readonly IPlanAccessRepository _repository;
    public PlanAccessService(IPlanAccessRepository repository) => _repository = repository;

    public async Task<PlanEntitlements> GetEntitlementsAsync(Guid idEtablissement, CancellationToken cancellationToken)
    {
        var formule = await _repository.IsProAsync(idEtablissement, cancellationToken) ? "PRO" : "FREE";
        return await _repository.GetEntitlementsAsync(formule, cancellationToken)
            ?? throw new InvalidOperationException($"Configuration de formule {formule} introuvable.");
    }

    public Task<bool> IsProAsync(Guid idEtablissement, CancellationToken cancellationToken) =>
        _repository.IsProAsync(idEtablissement, cancellationToken);

    public Task<IReadOnlyList<PlanEntitlements>> ListAsync(CancellationToken cancellationToken) =>
        _repository.ListEntitlementsAsync(cancellationToken);

    public Task<bool> UpdateAsync(PlanEntitlements entitlements, CancellationToken cancellationToken) =>
        _repository.UpdateEntitlementsAsync(entitlements, cancellationToken);

    public async Task<PlanUsage> GetUsageAsync(Guid idEtablissement, CancellationToken cancellationToken)
    {
        var droits = await GetEntitlementsAsync(idEtablissement, cancellationToken);
        return new PlanUsage
        {
            Formule = droits.Formule,
            LimitePrestations = droits.LimitePrestations,
            LimiteRdvMensuels = droits.LimiteRdvMensuels,
            PaiementMobile = droits.PaiementMobile,
            GestionEquipe = droits.GestionEquipe,
            StatistiquesAvancees = droits.StatistiquesAvancees,
            PrestationsUtilisees = await _repository.CountPrestationsAsync(idEtablissement, cancellationToken),
            RendezVousMoisUtilises = await _repository.CountRendezVousCurrentMonthAsync(idEtablissement, cancellationToken),
        };
    }

    public async Task<bool> CanAddPrestationAsync(Guid idEtablissement, CancellationToken cancellationToken)
    {
        var droits = await GetEntitlementsAsync(idEtablissement, cancellationToken);
        return droits.LimitePrestations is null || await _repository.CountPrestationsAsync(idEtablissement, cancellationToken) < droits.LimitePrestations;
    }

    public async Task<bool> CanCreateBookingAsync(Guid idEtablissement, CancellationToken cancellationToken)
    {
        var droits = await GetEntitlementsAsync(idEtablissement, cancellationToken);
        return droits.LimiteRdvMensuels is null || await _repository.CountRendezVousCurrentMonthAsync(idEtablissement, cancellationToken) < droits.LimiteRdvMensuels;
    }
}
