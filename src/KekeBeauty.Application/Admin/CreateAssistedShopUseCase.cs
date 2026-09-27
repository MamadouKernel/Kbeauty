namespace KekeBeauty.Application.Admin;

public sealed record AssistedShopRequest(
    string Gerant,
    string Telephone,
    string NomBoutique,
    string Contact,
    string? Adresse,
    string? Categorie,
    decimal Latitude,
    decimal Longitude);

public sealed record AssistedShopResult(
    bool Success,
    string Status,
    Guid? IdEtablissement = null,
    string? Message = null);

public sealed class CreateAssistedShopUseCase
{
    private readonly IAdminManagementRepository _repository;

    public CreateAssistedShopUseCase(IAdminManagementRepository repository)
    {
        _repository = repository;
    }

    public async Task<AssistedShopResult> ExecuteAsync(
        Guid adminId,
        AssistedShopRequest request,
        CancellationToken cancellationToken)
    {
        var normalized = Normalize(request);
        var validationError = Validate(normalized);
        if (validationError is not null)
        {
            return new AssistedShopResult(false, "donnees_invalides", Message: validationError);
        }

        var id = await _repository.CreateAssistedShopAsync(adminId, normalized, cancellationToken);
        return new AssistedShopResult(true, "EN_ATTENTE", id);
    }

    public static AssistedShopRequest Normalize(AssistedShopRequest request) => request with
    {
        Gerant = request.Gerant?.Trim() ?? string.Empty,
        Telephone = NormalizePhone(request.Telephone),
        NomBoutique = request.NomBoutique?.Trim() ?? string.Empty,
        Contact = NormalizePhone(request.Contact),
        Adresse = NullIfWhiteSpace(request.Adresse),
        Categorie = NullIfWhiteSpace(request.Categorie),
    };

    public static string? Validate(AssistedShopRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Gerant)) return "Le nom du gérant est obligatoire.";
        if (request.Telephone.Length < 10) return "Le téléphone du gérant est invalide.";
        if (string.IsNullOrWhiteSpace(request.NomBoutique)) return "Le nom de la boutique est obligatoire.";
        if (request.Contact.Length < 10) return "Le téléphone public de la boutique est invalide.";
        if (request.Latitude is < -90 or > 90) return "La latitude est invalide.";
        if (request.Longitude is < -180 or > 180) return "La longitude est invalide.";
        return null;
    }

    private static string NormalizePhone(string? value) =>
        new((value ?? string.Empty).Where(char.IsDigit).ToArray());

    private static string? NullIfWhiteSpace(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
