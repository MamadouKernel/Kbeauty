using FluentAssertions;
using KekeBeauty.Infrastructure.Admin;

namespace KekeBeauty.UnitTests;

public sealed class BackofficeTests
{
    [Fact]
    public void Templates_replace_known_variables_and_blank_the_missing_ones()
    {
        var variables = new Dictionary<string, string> { ["etablissement"] = "Salon Un", ["date"] = "12/10/2026" };

        TemplateRenderer.Render("Chez {etablissement} le {date} {heure}", variables).Should().Be("Chez Salon Un le 12/10/2026");
    }

    [Theory]
    [InlineData("Bonjour {client}", false)]
    [InlineData("Accolade {client", true)]
    [InlineData("Accolade client}", true)]
    [InlineData("Sans variable", false)]
    public void Stray_braces_are_detected(string text, bool stray) =>
        TemplateRenderer.HasStrayBraces(text).Should().Be(stray);

    [Fact]
    public void Every_catalog_default_only_uses_its_declared_variables()
    {
        foreach (var template in NotificationTemplateCatalog.All)
        {
            TemplateRenderer.Variables(template.DefaultTitle + " " + template.DefaultBody).Should().BeSubsetOf(template.Variables, template.Code);
            TemplateRenderer.HasStrayBraces(template.DefaultBody).Should().BeFalse(template.Code);
        }

        NotificationTemplateCatalog.All.Select(t => t.Code).Should().OnlyHaveUniqueItems();
    }

    [Theory]
    [InlineData("Esthétique / Soins visage", "esthetique-soins-visage")]
    [InlineData("  Bar à Ongles  ", "bar-a-ongles")]
    [InlineData("Spa & Détente", "spa-detente")]
    public void Category_slugs_are_ascii_and_hyphenated(string name, string slug) =>
        AdminSettingsService.Slugify(name).Should().Be(slug);

    [Theory]
    [InlineData("Salon Un", "Salon Un")]
    [InlineData("=SUM(A1)", "'=SUM(A1)")]
    [InlineData("a;b", "\"a;b\"")]
    [InlineData("dit \"oui\"", "\"dit \"\"oui\"\"\"")]
    public void Csv_cells_are_quoted_and_formulas_neutralised(string value, string expected) =>
        AdminFinanceService.Cell(value).Should().Be(expected);

    [Theory]
    [InlineData("50%", @"%50\%%")]
    [InlineData("a_b", @"%a\_b%")]
    public void Search_text_cannot_act_as_a_wildcard(string text, string like) =>
        AdminPaging.Like(text).Should().Be(like);

    [Theory]
    [InlineData(null, null, 1, 20)]
    [InlineData(0, 500, 1, 100)]
    [InlineData(3, 10, 3, 10)]
    public void Paging_is_clamped(int? page, int? size, int expectedPage, int expectedSize) =>
        AdminPaging.Normalize(page, size).Should().Be((expectedPage, expectedSize));
}
