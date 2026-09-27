using KekeBeauty.Application.Admin;
using KekeBeauty.Application.Onboarding;

namespace KekeBeauty.Api.Tests;

public sealed class BusinessRulesTests
{
    [Fact]
    public void AssistedShop_NormalizesPhoneAndWhitespace()
    {
        var request = new AssistedShopRequest(
            "  Awa Koné  ", "+225 07 01 02 03 04", "  Salon Awa  ",
            "+225 05 06 07 08 09", "  Cocody  ", "  Coiffure & Tresses  ", 5.36m, -4.01m);

        var normalized = CreateAssistedShopUseCase.Normalize(request);

        Assert.Equal("Awa Koné", normalized.Gerant);
        Assert.Equal("2250701020304", normalized.Telephone);
        Assert.Equal("Salon Awa", normalized.NomBoutique);
        Assert.Null(CreateAssistedShopUseCase.Validate(normalized));
    }

    [Fact]
    public void AssistedShop_RejectsInvalidCoordinates()
    {
        var request = new AssistedShopRequest(
            "Awa", "2250701020304", "Salon Awa", "2250506070809",
            null, null, 95m, -4.01m);

        Assert.Equal("La latitude est invalide.", CreateAssistedShopUseCase.Validate(request));
    }

    [Fact]
    public void PartnerApplication_RequiresExplicitPrivacyConsent()
    {
        using var storefront = new MemoryStream([1]);
        using var identity = new MemoryStream([1]);
        var submission = ValidPassportSubmission(storefront, identity) with { ConsentementRgpd = false };

        var error = PartnerApplicationValidator.Validate(submission);

        Assert.Equal("Votre accord sur le traitement des données du dossier est nécessaire.", error);
    }

    [Fact]
    public void PartnerApplication_AcceptsValidPassportSubmission()
    {
        using var storefront = new MemoryStream([1]);
        using var identity = new MemoryStream([1]);

        var error = PartnerApplicationValidator.Validate(ValidPassportSubmission(storefront, identity));

        Assert.Null(error);
    }

    private static PartnerApplicationSubmission ValidPassportSubmission(Stream storefront, Stream identity) =>
        new(
            "2250701020304", "Salon Awa", 5.36m, -4.01m, "2250506070809", null,
            storefront, "jpg", "PASSEPORT", identity, "pdf", null, null,
            "ESPECES", false, false, false,
            AuthenticatedPartnerId: Guid.NewGuid(), ConsentementRgpd: true);
}
