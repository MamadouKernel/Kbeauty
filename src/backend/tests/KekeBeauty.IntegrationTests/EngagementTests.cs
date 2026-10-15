using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using KekeBeauty.Infrastructure.Persistence;
using KekeBeauty.Infrastructure.Reviews;
using KekeBeauty.Infrastructure.Scheduling;
using KekeBeauty.Infrastructure.Search;
using KekeBeauty.Modules.Scheduling.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace KekeBeauty.IntegrationTests;

[Collection(ApiCollection.Name)]
public sealed class EngagementTests(ApiFixture api)
{
    private const string Phone = "07 00 00 12 34";

    private async Task WithDbAsync(Func<KekeBeautyDbContext, Task> action)
    {
        await using var scope = api.Services.CreateAsyncScope();
        await action(scope.ServiceProvider.GetRequiredService<KekeBeautyDbContext>());
    }

    private async Task<AppointmentDto> BookAsync(UserSession client, Salon salon)
    {
        var response = await client.Client.PostAsJsonAsync("/api/v1/appointments",
            new BookingRequest(salon.Id, [salon.ServiceHalfHour], null, TestSalons.NextOpenDay(api, 3), Phone, null));
        response.StatusCode.Should().Be(HttpStatusCode.Created, await response.Content.ReadAsStringAsync());
        return (await response.Content.ReadFromJsonAsync<AppointmentDto>())!;
    }

    /// <summary>The visit took place <paramref name="daysAgo"/> days ago.</summary>
    private Task CompleteAsync(Guid appointmentId, int daysAgo = 2)
    {
        var start = api.Clock.GetUtcNow().AddDays(-daysAgo);
        return WithDbAsync(db => db.Appointments.Where(a => a.Id == appointmentId).ExecuteUpdateAsync(set => set
            .SetProperty(a => a.Status, AppointmentStatus.Completed)
            .SetProperty(a => a.StartsAt, start)
            .SetProperty(a => a.EndsAt, start.AddMinutes(30))));
    }

    private static Task<HttpResponseMessage> ReviewAsync(UserSession client, Guid appointmentId, int rating, string? comment = null) =>
        client.Client.PostAsJsonAsync($"/api/v1/appointments/{appointmentId}/review", new ReviewInput(rating, comment));

    [Fact]
    public async Task A_client_reviews_a_completed_visit_once_the_owner_answers_and_the_rating_shows_everywhere()
    {
        using var admin = await TestAccounts.AdminAsync(api);
        var salon = await TestSalons.PublishAsync(api, admin, autoConfirm: true);
        using var client = await TestAccounts.ClientAsync(api);
        using var visitor = api.CreateClient();
        try
        {
            var appointment = await BookAsync(client, salon);

            // Not completed yet: nothing to review.
            var early = await ReviewAsync(client, appointment.Id, 5, "Parfait");
            early.StatusCode.Should().Be(HttpStatusCode.Conflict);
            (await early.Content.ReadAsStringAsync()).Should().Contain("opinion.not_reviewable");

            await CompleteAsync(appointment.Id);
            var created = await ReviewAsync(client, appointment.Id, 4, "  Très bon accueil et coiffure soignée.  ");
            created.StatusCode.Should().Be(HttpStatusCode.Created, await created.Content.ReadAsStringAsync());
            var review = (await created.Content.ReadFromJsonAsync<ReviewDto>())!;
            review.AuthorName.Should().Be("Awa K."); // first name and the initial only
            review.Comment.Should().Be("Très bon accueil et coiffure soignée.");

            // One review per visit.
            var again = await ReviewAsync(client, appointment.Id, 1);
            again.StatusCode.Should().Be(HttpStatusCode.Conflict);
            (await again.Content.ReadAsStringAsync()).Should().Contain("opinion.already_reviewed");

            // The page everybody sees, the rating on the card and on the fiche.
            var page = (await visitor.GetFromJsonAsync<ReviewsPage>($"/api/v1/establishments/{salon.Slug}/reviews"))!;
            page.Summary.Should().BeEquivalentTo(new RatingSummary(4.0, 1, [0, 0, 0, 1, 0]));
            page.Items.Should().ContainSingle().Which.Id.Should().Be(review.Id);
            var details = (await visitor.GetFromJsonAsync<EstablishmentDetails>($"/api/v1/establishments/{salon.Slug}"))!;
            details.Rating.Should().Be(4.0);
            details.ReviewCount.Should().Be(1);
            var listed = (await visitor.GetFromJsonAsync<EstablishmentPage>($"/api/v1/establishments?q={Uri.EscapeDataString(details.Name)}"))!;
            listed.Items.Single(i => i.Id == salon.Id).Should().Match<EstablishmentSummary>(i => i.Rating == 4.0 && i.ReviewCount == 1);

            // "Mieux notés": the reviewed salon comes first, rated ones before the others.
            var best = (await visitor.GetFromJsonAsync<EstablishmentPage>("/api/v1/establishments?sort=rating&pageSize=50"))!;
            best.Items.First().Id.Should().Be(salon.Id);
            best.Items.TakeWhile(i => i.Rating != null).Select(i => i.Rating!.Value).Should().BeInDescendingOrder();

            // The client sees what they already reviewed.
            (await client.Client.GetFromJsonAsync<List<MyReviewDto>>("/api/v1/me/reviews"))!.Should().ContainSingle(r => r.AppointmentId == appointment.Id && r.Rating == 4);

            // The owner answers (nobody else can), and the answer is public.
            using var stranger = await TestAccounts.PartnerAsync(api);
            (await stranger.Client.PostAsJsonAsync($"/api/v1/pro/reviews/{review.Id}/reply", new ReplyInput("Merci !"))).StatusCode.Should().Be(HttpStatusCode.NotFound);
            (await salon.Owner.Client.PostAsJsonAsync($"/api/v1/pro/reviews/{review.Id}/reply", new ReplyInput(" "))).StatusCode.Should().Be(HttpStatusCode.BadRequest);
            var replied = await salon.Owner.Client.PostAsJsonAsync($"/api/v1/pro/reviews/{review.Id}/reply", new ReplyInput("Merci beaucoup, à bientôt !"));
            replied.StatusCode.Should().Be(HttpStatusCode.OK, await replied.Content.ReadAsStringAsync());
            (await visitor.GetFromJsonAsync<ReviewsPage>($"/api/v1/establishments/{salon.Slug}/reviews"))!.Items.Single().ReplyText.Should().Be("Merci beaucoup, à bientôt !");
            (await stranger.Client.GetAsync($"/api/v1/pro/establishments/{salon.Id}/reviews")).StatusCode.Should().Be(HttpStatusCode.NotFound);
            (await salon.Owner.Client.GetFromJsonAsync<ReviewsPage>($"/api/v1/pro/establishments/{salon.Id}/reviews"))!.Items.Should().ContainSingle();

            // An administrator hides it (a reason is required): it leaves the public page and the rating, and can be restored.
            (await admin.Client.PostAsJsonAsync($"/api/v1/admin/reviews/{review.Id}/hide", new HideReviewInput(" "))).StatusCode.Should().Be(HttpStatusCode.BadRequest);
            (await client.Client.PostAsJsonAsync($"/api/v1/admin/reviews/{review.Id}/hide", new HideReviewInput("abus"))).StatusCode.Should().Be(HttpStatusCode.Forbidden);
            (await admin.Client.PostAsJsonAsync($"/api/v1/admin/reviews/{review.Id}/hide", new HideReviewInput("Propos injurieux"))).StatusCode.Should().Be(HttpStatusCode.NoContent);
            var hidden = (await visitor.GetFromJsonAsync<ReviewsPage>($"/api/v1/establishments/{salon.Slug}/reviews"))!;
            hidden.Items.Should().BeEmpty();
            hidden.Summary.Count.Should().Be(0);
            (await admin.Client.PostAsync($"/api/v1/admin/reviews/{review.Id}/restore", null)).StatusCode.Should().Be(HttpStatusCode.NoContent);
            (await visitor.GetFromJsonAsync<ReviewsPage>($"/api/v1/establishments/{salon.Slug}/reviews"))!.Summary.Count.Should().Be(1);
        }
        finally
        {
            await TestCleanup.DeleteEstablishmentsAsync(api, salon.Owner.UserId);
        }
    }

    [Fact]
    public async Task A_review_is_refused_when_the_note_is_wrong_the_comment_too_long_the_visit_not_theirs_or_too_old()
    {
        using var admin = await TestAccounts.AdminAsync(api);
        var salon = await TestSalons.PublishAsync(api, admin, autoConfirm: true);
        using var client = await TestAccounts.ClientAsync(api);
        using var other = await TestAccounts.ClientAsync(api);
        try
        {
            var appointment = await BookAsync(client, salon);
            await CompleteAsync(appointment.Id);

            (await ReviewAsync(client, appointment.Id, 0)).StatusCode.Should().Be(HttpStatusCode.BadRequest);
            (await ReviewAsync(client, appointment.Id, 6)).StatusCode.Should().Be(HttpStatusCode.BadRequest);
            (await ReviewAsync(client, appointment.Id, 3, new string('x', 1001))).StatusCode.Should().Be(HttpStatusCode.BadRequest);
            (await ReviewAsync(other, appointment.Id, 5)).StatusCode.Should().Be(HttpStatusCode.NotFound); // somebody else's visit
            using var anonymous = api.CreateClient();
            (await anonymous.PostAsJsonAsync($"/api/v1/appointments/{appointment.Id}/review", new ReviewInput(5, null))).StatusCode.Should().Be(HttpStatusCode.Unauthorized);

            await CompleteAsync(appointment.Id, daysAgo: 100);
            var late = await ReviewAsync(client, appointment.Id, 5);
            late.StatusCode.Should().Be(HttpStatusCode.Conflict);
            (await late.Content.ReadAsStringAsync()).Should().Contain("opinion.too_late");

            // No comment is fine: a rating alone counts.
            await CompleteAsync(appointment.Id, daysAgo: 1);
            (await ReviewAsync(client, appointment.Id, 5)).StatusCode.Should().Be(HttpStatusCode.Created);
        }
        finally
        {
            await TestCleanup.DeleteEstablishmentsAsync(api, salon.Owner.UserId);
        }
    }

    [Fact]
    public async Task A_client_saves_and_removes_favorites_and_sees_them_as_cards()
    {
        using var admin = await TestAccounts.AdminAsync(api);
        var salon = await TestSalons.PublishAsync(api, admin);
        using var client = await TestAccounts.ClientAsync(api);
        using var anonymous = api.CreateClient();
        try
        {
            (await anonymous.GetAsync("/api/v1/me/favorites")).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
            (await client.Client.PutAsync($"/api/v1/me/favorites/{Guid.NewGuid()}", null)).StatusCode.Should().Be(HttpStatusCode.NotFound);

            (await client.Client.PutAsync($"/api/v1/me/favorites/{salon.Id}", null)).StatusCode.Should().Be(HttpStatusCode.NoContent);
            (await client.Client.PutAsync($"/api/v1/me/favorites/{salon.Id}", null)).StatusCode.Should().Be(HttpStatusCode.NoContent); // twice is fine

            (await client.Client.GetFromJsonAsync<List<Guid>>("/api/v1/me/favorites/ids"))!.Should().Equal(salon.Id);
            var cards = (await client.Client.GetFromJsonAsync<List<EstablishmentSummary>>("/api/v1/me/favorites"))!;
            cards.Should().ContainSingle().Which.Should().Match<EstablishmentSummary>(c => c.Id == salon.Id && c.Phone != "" && c.ServiceCount > 0);

            (await client.Client.DeleteAsync($"/api/v1/me/favorites/{salon.Id}")).StatusCode.Should().Be(HttpStatusCode.NoContent);
            (await client.Client.GetFromJsonAsync<List<Guid>>("/api/v1/me/favorites/ids"))!.Should().BeEmpty();
        }
        finally
        {
            await TestCleanup.DeleteEstablishmentsAsync(api, salon.Owner.UserId);
        }
    }
}
