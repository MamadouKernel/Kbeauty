using FluentAssertions;
using KekeBeauty.Infrastructure.Growth;
using KekeBeauty.Infrastructure.Search;

namespace KekeBeauty.UnitTests;

public sealed class GrowthRulesTests
{
    [Theory]
    [InlineData(0, 10, 0)]
    [InlineData(999, 10, 0)]
    [InlineData(1000, 10, 10)]
    [InlineData(5000, 10, 50)]
    [InlineData(12500, 3, 36)]
    public void Loyalty_points_count_every_full_thousand(decimal price, int rate, int expected) =>
        LoyaltyService.PointsFor(price, rate).Should().Be(expected);

    [Fact]
    public void Gift_card_codes_are_readable_and_unique_enough()
    {
        var codes = Enumerable.Range(0, 2000).Select(_ => GiftCardService.NewCode()).ToList();

        codes.Should().OnlyContain(c => System.Text.RegularExpressions.Regex.IsMatch(c, "^KB-[2-9A-HJKMNP-Z]{4}-[2-9A-HJKMNP-Z]{4}$"));
        codes.Should().OnlyHaveUniqueItems();
    }

    [Theory]
    [InlineData("KB-7HQ4-M2XP", "KB-7HQ4-M2XP")]
    [InlineData("kb 7hq4 m2xp", "KB-7HQ4-M2XP")]
    [InlineData("7HQ4M2XP", "KB-7HQ4-M2XP")]
    [InlineData("KB-7HQ4-M2X", null)]
    [InlineData("KB-7HQ4-M2X0", null)]
    [InlineData("KB-7HQ4-M2XI", null)]
    [InlineData("", null)]
    [InlineData(null, null)]
    public void Gift_card_codes_are_accepted_as_people_type_them(string? typed, string? expected) =>
        GiftCardService.NormalizeCode(typed).Should().Be(expected);

    private static EstablishmentSummary Place(double? km, double? rating = null, int reviews = 0, params string[] categories) => new(
        Guid.NewGuid(), "slug", "Nom", null, categories.Select(c => new CategoryRef(c, c)).ToList(), "Cocody", "Abidjan", km,
        5.3, -4.0, null, null, false, false, "+225", "Rue", 1, rating, reviews);

    [Fact]
    public void Closer_places_score_higher_all_else_equal()
    {
        var none = new Dictionary<string, int>();

        RecommendationService.Score(Place(0.5), none, located: true)
            .Should().BeGreaterThan(RecommendationService.Score(Place(8), none, located: true));
        RecommendationService.Score(Place(RecommendationService.RadiusKm), none, located: true).Should().Be(0);
    }

    [Fact]
    public void Liked_categories_and_good_ratings_lift_a_place()
    {
        var liked = new Dictionary<string, int> { ["onglerie"] = 2 };

        RecommendationService.Score(Place(5, categories: "onglerie"), liked, located: true)
            .Should().BeGreaterThan(RecommendationService.Score(Place(5, categories: "spa"), liked, located: true));
        RecommendationService.Score(Place(5, 4.8, 10), liked, located: true)
            .Should().BeGreaterThan(RecommendationService.Score(Place(5, 2.5, 10), liked, located: true));
    }
}
