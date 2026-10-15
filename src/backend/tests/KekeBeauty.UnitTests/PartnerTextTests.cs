using FluentAssertions;
using KekeBeauty.Infrastructure.Partner;
using KekeBeauty.Modules.Onboarding.Domain;

namespace KekeBeauty.UnitTests;

public sealed class PartnerTextTests
{
    [Theory]
    [InlineData("Maison Éclat Coiffure", "maison-eclat-coiffure")]
    [InlineData("  Spa  Lagune Bleue !! ", "spa-lagune-bleue")]
    [InlineData("Onglerie Perle d'Ivoire", "onglerie-perle-d-ivoire")]
    [InlineData("Cœur d'Ébène", "coeur-d-ebene")]
    [InlineData("%%%", "etablissement")]
    public void Slugify_produces_a_clean_ascii_slug(string name, string expected) =>
        PartnerText.Slugify(name).Should().Be(expected);

    [Fact]
    public void Slugify_caps_the_length()
    {
        PartnerText.Slugify(new string('a', 400)).Length.Should().BeLessThanOrEqualTo(120);
    }

    [Theory]
    [InlineData("Awa Koné", "kone awa", true)]
    [InlineData("AWA  KONE", "Awa Koné", true)]
    [InlineData("Awa Koné", "Awa Traoré", false)]
    [InlineData("Awa", "Awa Koné", false)]
    public void SameName_ignores_case_accents_and_order(string a, string b, bool expected) =>
        PartnerText.SameName(a, b).Should().Be(expected);

    [Theory]
    [InlineData("07 00 00 00 99", "0700000099")]
    [InlineData("+225 07-00-00-00-99", "+2250700000099")]
    [InlineData("+22507.00.00.00.99", "+2250700000099")]
    public void NormalizePhone_strips_separators(string input, string expected) =>
        PartnerText.NormalizePhone(input).Should().Be(expected);

    [Theory]
    [InlineData("")]
    [InlineData("abc")]
    [InlineData("123")]
    [InlineData("+1234567890123456789")]
    [InlineData("07 00 00 00 99 <script>")]
    public void NormalizePhone_rejects_implausible_numbers(string input) =>
        PartnerText.NormalizePhone(input).Should().BeNull();

    [Fact]
    public void NormalizeDocumentNumber_removes_separators_and_uppercases() =>
        PartnerText.NormalizeDocumentNumber("ci 0012-3456.7").Should().Be("CI001234567");

    private static KycIdentity IdentityWith(DateOnly? expiry, string holder = "Awa Koné", bool documents = true) => new()
    {
        UserId = Guid.NewGuid(),
        HolderName = holder,
        DocumentNumber = "CI001234567",
        DocumentExpiry = expiry,
        FrontKey = documents ? "front" : null,
        BackKey = documents ? "back" : null,
    };

    private static readonly DateOnly Today = new(2026, 10, 11);

    [Fact]
    public void A_valid_unique_matching_identity_has_no_blocking_check()
    {
        var checks = PartnerText.EvaluateKyc(IdentityWith(new DateOnly(2030, 1, 1)), "Awa Koné", duplicateDocumentNumber: false, Today);

        checks.Select(c => c.Code).Should().BeEquivalentTo("document_valid", "name_match", "document_unique");
        checks.Should().OnlyContain(c => c.Level == "ok");
    }

    [Fact]
    public void An_expired_duplicated_mismatching_identity_is_flagged()
    {
        var checks = PartnerText.EvaluateKyc(IdentityWith(new DateOnly(2020, 1, 1), "Moussa Traoré", documents: false), "Awa Koné", duplicateDocumentNumber: true, Today);

        checks.Should().Contain(c => c.Code == "document_expired" && c.Level == "error");
        checks.Should().Contain(c => c.Code == "name_mismatch" && c.Level == "warning");
        checks.Should().Contain(c => c.Code == "duplicate_document" && c.Level == "error");
        checks.Should().Contain(c => c.Code == "documents_missing" && c.Level == "error");
    }

    [Fact]
    public void A_missing_expiry_is_only_a_warning()
    {
        var checks = PartnerText.EvaluateKyc(IdentityWith(null), "Awa Koné", duplicateDocumentNumber: false, Today);

        checks.Should().Contain(c => c.Code == "expiry_missing" && c.Level == "warning");
    }

    [Fact]
    public void An_identity_expiring_today_is_still_valid()
    {
        var checks = PartnerText.EvaluateKyc(IdentityWith(Today), "Awa Koné", duplicateDocumentNumber: false, Today);

        checks.Should().Contain(c => c.Code == "document_valid");
    }

    [Fact]
    public void Media_urls_route_uploads_through_the_api_and_demo_files_through_the_web_root()
    {
        MediaUrls.Build("/media", "uploads/abc/def.webp").Should().Be("/api/v1/files/uploads/abc/def.webp");
        MediaUrls.Build("/media", "demo/x.svg").Should().Be("/media/demo/x.svg");
        MediaUrls.Build("https://cdn.example", "/demo/x.svg").Should().Be("https://cdn.example/demo/x.svg");
    }

    [Fact]
    public void Missing_items_are_computed_from_what_the_wizard_has_collected()
    {
        PartnerSupport.ComputeMissing(1, 0, 0, null).Select(m => m.Code)
            .Should().Equal("hours", "photo", "identity", "identity_documents");

        PartnerSupport.ComputeMissing(0, 3, 1, IdentityWith(null)).Select(m => m.Code).Should().Equal("category");

        var approved = IdentityWith(null, documents: false);
        approved.Status = KycStatus.Approved;
        PartnerSupport.ComputeMissing(1, 3, 1, approved).Should().BeEmpty();
    }
}
