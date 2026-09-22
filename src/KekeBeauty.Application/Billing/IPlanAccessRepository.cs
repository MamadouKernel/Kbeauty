namespace KekeBeauty.Application.Billing;

public class PlanEntitlements
{
    public string Formule { get; set; } = "FREE";
    public string Libelle { get; set; } = "";
    public bool EstActif { get; set; } = true;
    public bool EstDefaut { get; set; }
    public int OrdreAffichage { get; set; }
    public decimal? TarifMensuel { get; set; }
    public decimal? TarifAnnuel { get; set; }
    public int? LimitePrestations { get; set; }
    public int? LimiteRdvMensuels { get; set; }
    public bool PaiementMobile { get; set; }
    public bool GestionEquipe { get; set; }
    public bool StatistiquesAvancees { get; set; }
    /// <summary>Texte libre de mise en avant, entierement au choix de l'admin (non applique
    /// techniquement - voir les champs ci-dessus pour ce qui est reellement impose).</summary>
    public List<string> Avantages { get; set; } = [];
    public int? PromoPourcentage { get; set; }
    /// <summary>Fin de la promotion ; null = indefinie (active jusqu'a suppression manuelle).</summary>
    public DateTimeOffset? PromoFin { get; set; }

    public bool EstPromoActive => PromoPourcentage is > 0 && (PromoFin is null || PromoFin > DateTimeOffset.UtcNow);
    public decimal? TarifMensuelEffectif => AppliquerPromo(TarifMensuel);
    public decimal? TarifAnnuelEffectif => AppliquerPromo(TarifAnnuel);

    private decimal? AppliquerPromo(decimal? tarif) =>
        tarif is null ? null : EstPromoActive ? Math.Round(tarif.Value * (100 - PromoPourcentage!.Value) / 100m, 0) : tarif;
}

public sealed class PlanUsage : PlanEntitlements
{
    public int PrestationsUtilisees { get; init; }
    public int RendezVousMoisUtilises { get; init; }
}

public enum DeleteFormuleResult
{
    Deleted,
    NotFound,
    EstFormuleParDefaut,
    DerniereFormule,
    FormuleUtilisee,
}

public interface IPlanAccessRepository
{
    /// <summary>Formule effective de la boutique : celle de son abonnement ACTIF le plus recent,
    /// sinon la formule marquee "par defaut" (attribuee aux boutiques sans abonnement payant).</summary>
    Task<string> GetFormuleActuelleAsync(Guid idEtablissement, CancellationToken cancellationToken);
    Task<PlanEntitlements?> GetEntitlementsAsync(string formule, CancellationToken cancellationToken);
    Task<IReadOnlyList<PlanEntitlements>> ListEntitlementsAsync(CancellationToken cancellationToken);
    Task<bool> UpdateEntitlementsAsync(PlanEntitlements entitlements, CancellationToken cancellationToken);
    Task<bool> CreateEntitlementsAsync(PlanEntitlements entitlements, CancellationToken cancellationToken);
    Task<DeleteFormuleResult> DeleteEntitlementsAsync(string formule, CancellationToken cancellationToken);
    Task<bool> SetDefaultAsync(string formule, CancellationToken cancellationToken);
    Task<int> CountPrestationsAsync(Guid idEtablissement, CancellationToken cancellationToken);
    Task<int> CountRendezVousCurrentMonthAsync(Guid idEtablissement, CancellationToken cancellationToken);
}

public sealed class PlanAccessService
{
    private readonly IPlanAccessRepository _repository;
    public PlanAccessService(IPlanAccessRepository repository) => _repository = repository;

    public async Task<PlanEntitlements> GetEntitlementsAsync(Guid idEtablissement, CancellationToken cancellationToken)
    {
        var formule = await _repository.GetFormuleActuelleAsync(idEtablissement, cancellationToken);
        return await _repository.GetEntitlementsAsync(formule, cancellationToken)
            ?? throw new InvalidOperationException($"Configuration de formule {formule} introuvable.");
    }

    public Task<PlanEntitlements?> GetEntitlementsAsync(string formule, CancellationToken cancellationToken) =>
        _repository.GetEntitlementsAsync(formule, cancellationToken);

    public Task<IReadOnlyList<PlanEntitlements>> ListAsync(CancellationToken cancellationToken) =>
        _repository.ListEntitlementsAsync(cancellationToken);

    public Task<bool> UpdateAsync(PlanEntitlements entitlements, CancellationToken cancellationToken) =>
        _repository.UpdateEntitlementsAsync(entitlements, cancellationToken);

    public Task<bool> CreateAsync(PlanEntitlements entitlements, CancellationToken cancellationToken) =>
        _repository.CreateEntitlementsAsync(entitlements, cancellationToken);

    public Task<DeleteFormuleResult> DeleteAsync(string formule, CancellationToken cancellationToken) =>
        _repository.DeleteEntitlementsAsync(formule, cancellationToken);

    public Task<bool> SetDefaultAsync(string formule, CancellationToken cancellationToken) =>
        _repository.SetDefaultAsync(formule, cancellationToken);

    public async Task<PlanUsage> GetUsageAsync(Guid idEtablissement, CancellationToken cancellationToken)
    {
        var droits = await GetEntitlementsAsync(idEtablissement, cancellationToken);
        return new PlanUsage
        {
            Formule = droits.Formule,
            Libelle = droits.Libelle,
            EstActif = droits.EstActif,
            EstDefaut = droits.EstDefaut,
            OrdreAffichage = droits.OrdreAffichage,
            TarifMensuel = droits.TarifMensuel,
            TarifAnnuel = droits.TarifAnnuel,
            LimitePrestations = droits.LimitePrestations,
            LimiteRdvMensuels = droits.LimiteRdvMensuels,
            PaiementMobile = droits.PaiementMobile,
            GestionEquipe = droits.GestionEquipe,
            StatistiquesAvancees = droits.StatistiquesAvancees,
            Avantages = droits.Avantages,
            PromoPourcentage = droits.PromoPourcentage,
            PromoFin = droits.PromoFin,
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
