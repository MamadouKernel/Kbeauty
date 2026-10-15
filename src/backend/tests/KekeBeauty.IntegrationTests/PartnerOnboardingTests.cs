using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using KekeBeauty.Api.Endpoints;
using KekeBeauty.Infrastructure.Partner;
using KekeBeauty.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace KekeBeauty.IntegrationTests;

[Collection(ApiCollection.Name)]
public sealed class PartnerOnboardingTests(ApiFixture api)
{
    private sealed record GeoItem(Guid Id, string Name);

    private sealed record CountryItem(Guid Id, string Name);

    private sealed record CategoryItem(Guid Id, string Name, string Slug, string Icon);

    private static readonly KycInput Identity = new("Awa Koné", "NationalId", "CI 0012 3456 7", new DateOnly(2032, 5, 1));

    private async Task<Guid> CommuneIdAsync(string name)
    {
        using var client = api.CreateClient();
        var country = (await client.GetFromJsonAsync<List<CountryItem>>("/api/v1/geo/countries"))!.Single();
        var regions = (await client.GetFromJsonAsync<List<GeoItem>>($"/api/v1/geo/countries/{country.Id}/regions"))!;
        var region = regions.Single(r => r.Name.Contains("Abidjan", StringComparison.Ordinal));
        var city = (await client.GetFromJsonAsync<List<GeoItem>>($"/api/v1/geo/regions/{region.Id}/cities"))!.Single();
        var communes = (await client.GetFromJsonAsync<List<GeoItem>>($"/api/v1/geo/cities/{city.Id}/communes"))!;
        return communes.Single(c => c.Name == name).Id;
    }

    private async Task<Guid> CategoryIdAsync(string slug)
    {
        using var client = api.CreateClient();
        return (await client.GetFromJsonAsync<List<CategoryItem>>("/api/v1/categories"))!.Single(c => c.Slug == slug).Id;
    }

    private async Task<EstablishmentInput> InputAsync(string name) => new(
        name,
        "Salon de test : tresses, soins et coupes dans un cadre soigné.",
        "07 00 00 00 99",
        "contact@example.com",
        [await CategoryIdAsync("salon-de-coiffure")],
        await CommuneIdAsync("Cocody"),
        "Rue des Jardins, Deux-Plateaux",
        5.3599,
        -3.9877);

    private static string UniqueName() => $"Salon Test {Guid.NewGuid():N}";

    private static async Task<string> CodeOfAsync(HttpResponseMessage response)
    {
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return document.RootElement.GetProperty("code").GetString()!;
    }

    private static async Task<Guid> CreateAsync(UserSession partner, EstablishmentInput input)
    {
        var response = await partner.Client.PostAsJsonAsync("/api/v1/pro/establishments", input);
        response.StatusCode.Should().Be(HttpStatusCode.Created);
        return (await response.Content.ReadFromJsonAsync<CreatedId>())!.Id;
    }

    private static async Task<PartnerEstablishmentDetail> DetailAsync(UserSession partner, Guid id) =>
        (await partner.Client.GetFromJsonAsync<PartnerEstablishmentDetail>($"/api/v1/pro/establishments/{id}"))!;

    /// <summary>Everything the wizard collects except the final submission.</summary>
    private async Task<Guid> CreateReadyAsync(UserSession partner, string name, bool withIdentity = true)
    {
        var id = await CreateAsync(partner, await InputAsync(name));

        var hours = Enumerable.Range(1, 6).Select(day => new OpeningHourInput(day, "09:00", "18:00")).ToList();
        (await partner.Client.PutAsJsonAsync($"/api/v1/pro/establishments/{id}/opening-hours", hours)).StatusCode.Should().Be(HttpStatusCode.NoContent);

        using var photo = TestAccounts.FileForm(TestAccounts.Png());
        (await partner.Client.PostAsync($"/api/v1/pro/establishments/{id}/media", photo)).StatusCode.Should().Be(HttpStatusCode.Created);

        if (withIdentity)
        {
            (await partner.Client.PutAsJsonAsync("/api/v1/pro/kyc", Identity)).StatusCode.Should().Be(HttpStatusCode.OK);
            foreach (var side in new[] { "front", "back" })
            {
                using var document = TestAccounts.FileForm(TestAccounts.Png(1200, 800), $"{side}.png");
                (await partner.Client.PutAsync($"/api/v1/pro/kyc/documents/{side}", document)).StatusCode.Should().Be(HttpStatusCode.OK);
            }
        }

        return id;
    }

    private async Task DeleteEstablishmentsOfAsync(params Guid[] ownerIds)
    {
        await using var scope = api.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<KekeBeautyDbContext>();
        await db.Establishments.Where(e => e.OwnerUserId != null && ownerIds.Contains(e.OwnerUserId.Value)).ExecuteDeleteAsync();
    }

    private async Task<QueueItem?> QueueItemAsync(UserSession admin, string establishmentName, string status = "PendingReview")
    {
        var page = (await admin.Client.GetFromJsonAsync<QueuePage>($"/api/v1/admin/kyc/queue?status={status}&pageSize=50"))!;
        return page.Items.FirstOrDefault(i => i.EstablishmentName == establishmentName);
    }

    [Fact]
    public async Task A_partner_journey_ends_with_the_establishment_published_after_admin_approval()
    {
        using var partner = await TestAccounts.PartnerAsync(api);
        using var admin = await TestAccounts.AdminAsync(api);
        using var anonymous = api.CreateClient();
        var name = UniqueName();

        try
        {
            var id = await CreateReadyAsync(partner, name);
            var draft = await DetailAsync(partner, id);
            draft.Status.Should().Be("Draft");
            draft.Missing.Should().BeEmpty();
            (await anonymous.GetAsync($"/api/v1/establishments/{draft.Slug}")).StatusCode.Should().Be(HttpStatusCode.NotFound);

            (await partner.Client.PostAsync($"/api/v1/pro/establishments/{id}/submit", null)).StatusCode.Should().Be(HttpStatusCode.OK);
            var pending = await DetailAsync(partner, id);
            pending.Status.Should().Be("PendingReview");
            pending.IsEditable.Should().BeFalse();
            (await anonymous.GetAsync($"/api/v1/establishments/{draft.Slug}")).StatusCode.Should().Be(HttpStatusCode.NotFound);

            var queued = await QueueItemAsync(admin, name);
            queued.Should().NotBeNull();
            queued!.OwnerEmail.Should().Be(partner.Email);

            var review = (await admin.Client.GetFromJsonAsync<ReviewDetail>($"/api/v1/admin/kyc/{queued.ReviewId}"))!;
            review.Identity!.HasFront.Should().BeTrue();
            review.Identity.HasBack.Should().BeTrue();
            review.Identity.DocumentNumber.Should().Be("CI001234567");
            review.Checks.Select(c => c.Code).Should().Contain(["name_match", "document_valid", "document_unique"]);

            var document = await admin.Client.GetAsync($"/api/v1/admin/kyc/{queued.ReviewId}/documents/front");
            document.StatusCode.Should().Be(HttpStatusCode.OK);
            document.Content.Headers.ContentType!.MediaType.Should().Be("image/webp");
            document.Headers.CacheControl!.NoStore.Should().BeTrue();
            await using (var scope = api.Services.CreateAsyncScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<KekeBeautyDbContext>();
                (await db.AuditLogs.CountAsync(a => a.EntityId == queued.ReviewId && a.Action == "kyc.document.view")).Should().Be(1);
            }

            var approve = await admin.Client.PostAsJsonAsync($"/api/v1/admin/kyc/{queued.ReviewId}/decision", new DecisionInput("Approve", null));
            approve.StatusCode.Should().Be(HttpStatusCode.OK);

            var live = await anonymous.GetAsync($"/api/v1/establishments/{draft.Slug}");
            live.StatusCode.Should().Be(HttpStatusCode.OK);
            var search = await anonymous.GetFromJsonAsync<KekeBeauty.Infrastructure.Search.EstablishmentPage>($"/api/v1/establishments?q={Uri.EscapeDataString(name)}");
            var summary = search!.Items.Should().ContainSingle().Subject;
            summary.CoverUrl.Should().StartWith("/api/v1/files/uploads/");

            var cover = await anonymous.GetAsync(summary.CoverUrl);
            cover.StatusCode.Should().Be(HttpStatusCode.OK);
            cover.Content.Headers.ContentType!.MediaType.Should().Be("image/webp");
            cover.Headers.CacheControl!.Public.Should().BeTrue();

            api.Emails.Messages.Should().Contain(m => m.To == partner.Email && m.Subject.Contains("en ligne", StringComparison.Ordinal));

            // ONB-04: the approved identity is reused, so a second establishment only needs hours and a photo.
            var second = await CreateReadyAsync(partner, UniqueName(), withIdentity: false);
            (await DetailAsync(partner, second)).Missing.Should().BeEmpty();
            (await partner.Client.GetFromJsonAsync<List<PartnerEstablishmentSummary>>("/api/v1/pro/establishments"))!.Should().HaveCount(2);
        }
        finally
        {
            await DeleteEstablishmentsOfAsync(partner.UserId);
        }
    }

    [Fact]
    public async Task Submitting_an_incomplete_file_is_refused_with_the_missing_items()
    {
        using var partner = await TestAccounts.PartnerAsync(api);

        try
        {
            var id = await CreateAsync(partner, await InputAsync(UniqueName()));

            var detail = await DetailAsync(partner, id);
            detail.Missing.Select(m => m.Code).Should().BeEquivalentTo("hours", "photo", "identity", "identity_documents");

            var submit = await partner.Client.PostAsync($"/api/v1/pro/establishments/{id}/submit", null);
            submit.StatusCode.Should().Be(HttpStatusCode.Conflict);
            (await CodeOfAsync(submit)).Should().Be("establishment.incomplete");
            (await DetailAsync(partner, id)).Status.Should().Be("Draft");
        }
        finally
        {
            await DeleteEstablishmentsOfAsync(partner.UserId);
        }
    }

    [Fact]
    public async Task Roles_and_ownership_are_enforced()
    {
        using var partner = await TestAccounts.PartnerAsync(api);
        using var other = await TestAccounts.PartnerAsync(api, "Moussa", "Traoré");
        using var customer = await TestAccounts.ClientAsync(api);
        using var anonymous = api.CreateClient();

        try
        {
            var id = await CreateAsync(partner, await InputAsync(UniqueName()));

            (await anonymous.GetAsync("/api/v1/pro/establishments")).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
            (await customer.Client.GetAsync("/api/v1/pro/establishments")).StatusCode.Should().Be(HttpStatusCode.Forbidden);
            (await partner.Client.GetAsync("/api/v1/admin/kyc/queue")).StatusCode.Should().Be(HttpStatusCode.Forbidden);
            (await anonymous.GetAsync("/api/v1/admin/kyc/queue")).StatusCode.Should().Be(HttpStatusCode.Unauthorized);

            (await other.Client.GetAsync($"/api/v1/pro/establishments/{id}")).StatusCode.Should().Be(HttpStatusCode.NotFound);
            (await other.Client.PutAsJsonAsync($"/api/v1/pro/establishments/{id}", await InputAsync("Piraté"))).StatusCode.Should().Be(HttpStatusCode.NotFound);
            (await other.Client.PostAsync($"/api/v1/pro/establishments/{id}/submit", null)).StatusCode.Should().Be(HttpStatusCode.NotFound);
            (await other.Client.GetFromJsonAsync<List<PartnerEstablishmentSummary>>("/api/v1/pro/establishments"))!.Should().BeEmpty();
        }
        finally
        {
            await DeleteEstablishmentsOfAsync(partner.UserId);
        }
    }

    [Fact]
    public async Task Requested_changes_reopen_the_file_and_a_rejection_locks_it()
    {
        using var partner = await TestAccounts.PartnerAsync(api);
        using var admin = await TestAccounts.AdminAsync(api);
        using var anonymous = api.CreateClient();
        var name = UniqueName();

        try
        {
            var id = await CreateReadyAsync(partner, name);
            (await partner.Client.PostAsync($"/api/v1/pro/establishments/{id}/submit", null)).StatusCode.Should().Be(HttpStatusCode.OK);
            var first = (await QueueItemAsync(admin, name))!;

            var noComment = await admin.Client.PostAsJsonAsync($"/api/v1/admin/kyc/{first.ReviewId}/decision", new DecisionInput("RequestChanges", " "));
            noComment.StatusCode.Should().Be(HttpStatusCode.BadRequest);
            var unknown = await admin.Client.PostAsJsonAsync($"/api/v1/admin/kyc/{first.ReviewId}/decision", new DecisionInput("Maybe", "x"));
            unknown.StatusCode.Should().Be(HttpStatusCode.BadRequest);

            var changes = await admin.Client.PostAsJsonAsync($"/api/v1/admin/kyc/{first.ReviewId}/decision", new DecisionInput("RequestChanges", "Photo de la devanture floue."));
            changes.StatusCode.Should().Be(HttpStatusCode.OK);
            (await admin.Client.PostAsJsonAsync($"/api/v1/admin/kyc/{first.ReviewId}/decision", new DecisionInput("Approve", null)))
                .StatusCode.Should().Be(HttpStatusCode.Conflict);

            var reopened = await DetailAsync(partner, id);
            reopened.Status.Should().Be("Draft");
            reopened.IsEditable.Should().BeTrue();
            reopened.LatestReview!.Status.Should().Be("ChangesRequested");
            reopened.LatestReview.Comment.Should().Be("Photo de la devanture floue.");
            (await partner.Client.GetFromJsonAsync<KycView>("/api/v1/pro/kyc"))!.Status.Should().Be("ChangesRequested");

            var edited = (await InputAsync(name)) with { Description = "Description corrigée après les remarques de l'équipe." };
            (await partner.Client.PutAsJsonAsync($"/api/v1/pro/establishments/{id}", edited)).StatusCode.Should().Be(HttpStatusCode.NoContent);
            (await partner.Client.PostAsync($"/api/v1/pro/establishments/{id}/submit", null)).StatusCode.Should().Be(HttpStatusCode.OK);

            var second = (await QueueItemAsync(admin, name))!;
            second.ReviewId.Should().NotBe(first.ReviewId);
            var reject = await admin.Client.PostAsJsonAsync($"/api/v1/admin/kyc/{second.ReviewId}/decision", new DecisionInput("Reject", "Pièce illisible."));
            reject.StatusCode.Should().Be(HttpStatusCode.OK);

            var rejected = await DetailAsync(partner, id);
            rejected.Status.Should().Be("Rejected");
            rejected.IsEditable.Should().BeFalse();
            (await partner.Client.PutAsJsonAsync($"/api/v1/pro/establishments/{id}", edited)).StatusCode.Should().Be(HttpStatusCode.Conflict);
            (await anonymous.GetAsync($"/api/v1/establishments/{rejected.Slug}")).StatusCode.Should().Be(HttpStatusCode.NotFound);
            api.Emails.Messages.Should().Contain(m => m.To == partner.Email && m.Subject.Contains("refusé", StringComparison.Ordinal));
        }
        finally
        {
            await DeleteEstablishmentsOfAsync(partner.UserId);
        }
    }

    [Fact]
    public async Task Uploads_are_validated_by_their_real_content_and_size()
    {
        using var partner = await TestAccounts.PartnerAsync(api);

        try
        {
            var id = await CreateAsync(partner, await InputAsync(UniqueName()));

            using var fake = TestAccounts.FileForm("<script>alert(1)</script>"u8.ToArray(), "photo.png", "image/png");
            var fakeResponse = await partner.Client.PostAsync($"/api/v1/pro/establishments/{id}/media", fake);
            fakeResponse.StatusCode.Should().Be(HttpStatusCode.BadRequest);
            (await CodeOfAsync(fakeResponse)).Should().Be("file.unsupported");

            using var huge = TestAccounts.FileForm(new byte[11 * 1024 * 1024]);
            (await partner.Client.PostAsync($"/api/v1/pro/establishments/{id}/media", huge)).StatusCode.Should().Be(HttpStatusCode.RequestEntityTooLarge);

            // Documents cannot be attached before the identity data exists.
            using var early = TestAccounts.FileForm(TestAccounts.Png());
            (await partner.Client.PutAsync("/api/v1/pro/kyc/documents/front", early)).StatusCode.Should().Be(HttpStatusCode.BadRequest);
            (await partner.Client.PutAsJsonAsync("/api/v1/pro/kyc", Identity with { DocumentType = "Library card" })).StatusCode.Should().Be(HttpStatusCode.BadRequest);
            (await partner.Client.PutAsJsonAsync("/api/v1/pro/kyc", Identity)).StatusCode.Should().Be(HttpStatusCode.OK);
            using var badSide = TestAccounts.FileForm(TestAccounts.Png());
            (await partner.Client.PutAsync("/api/v1/pro/kyc/documents/top", badSide)).StatusCode.Should().Be(HttpStatusCode.BadRequest);

            (await DetailAsync(partner, id)).Media.Should().BeEmpty();
        }
        finally
        {
            await DeleteEstablishmentsOfAsync(partner.UserId);
        }
    }

    [Fact]
    public async Task The_gallery_is_limited_and_keeps_a_cover()
    {
        using var partner = await TestAccounts.PartnerAsync(api);

        try
        {
            var id = await CreateAsync(partner, await InputAsync(UniqueName()));
            for (var i = 0; i < 5; i++)
            {
                using var photo = TestAccounts.FileForm(TestAccounts.Png(640 + i, 480));
                (await partner.Client.PostAsync($"/api/v1/pro/establishments/{id}/media", photo)).StatusCode.Should().Be(HttpStatusCode.Created);
            }

            using var sixth = TestAccounts.FileForm(TestAccounts.Png());
            var refused = await partner.Client.PostAsync($"/api/v1/pro/establishments/{id}/media", sixth);
            refused.StatusCode.Should().Be(HttpStatusCode.Conflict);
            (await CodeOfAsync(refused)).Should().Be("media.limit_reached");

            var media = (await DetailAsync(partner, id)).Media;
            media.Should().HaveCount(5);
            media.Count(m => m.IsCover).Should().Be(1);
            media[0].IsCover.Should().BeTrue();
            media[0].ThumbUrl.Should().Contain("_thumb");

            var newCover = media[2].Id;
            (await partner.Client.PutAsync($"/api/v1/pro/establishments/{id}/media/{newCover}/cover", null)).StatusCode.Should().Be(HttpStatusCode.NoContent);
            (await DetailAsync(partner, id)).Media.Single(m => m.IsCover).Id.Should().Be(newCover);

            (await partner.Client.DeleteAsync($"/api/v1/pro/establishments/{id}/media/{newCover}")).StatusCode.Should().Be(HttpStatusCode.NoContent);
            var after = (await DetailAsync(partner, id)).Media;
            after.Should().HaveCount(4);
            after.Count(m => m.IsCover).Should().Be(1);

            var reversed = after.Select(m => m.Id).Reverse().ToList();
            (await partner.Client.PutAsJsonAsync($"/api/v1/pro/establishments/{id}/media/order", new OrderInput(reversed))).StatusCode.Should().Be(HttpStatusCode.NoContent);
            (await partner.Client.DeleteAsync($"/api/v1/pro/establishments/{id}/media/{Guid.NewGuid()}")).StatusCode.Should().Be(HttpStatusCode.NotFound);
        }
        finally
        {
            await DeleteEstablishmentsOfAsync(partner.UserId);
        }
    }

    [Fact]
    public async Task Opening_hours_are_validated()
    {
        using var partner = await TestAccounts.PartnerAsync(api);

        try
        {
            var id = await CreateAsync(partner, await InputAsync(UniqueName()));
            var url = $"/api/v1/pro/establishments/{id}/opening-hours";

            (await partner.Client.PutAsJsonAsync(url, new[] { new OpeningHourInput(1, "18:00", "09:00") })).StatusCode.Should().Be(HttpStatusCode.BadRequest);
            (await partner.Client.PutAsJsonAsync(url, new[] { new OpeningHourInput(9, "09:00", "18:00") })).StatusCode.Should().Be(HttpStatusCode.BadRequest);
            (await partner.Client.PutAsJsonAsync(url, new[] { new OpeningHourInput(1, "9h", "18:00") })).StatusCode.Should().Be(HttpStatusCode.BadRequest);
            (await partner.Client.PutAsJsonAsync(url, new[] { new OpeningHourInput(1, "09:00", "13:00"), new OpeningHourInput(1, "12:00", "18:00") }))
                .StatusCode.Should().Be(HttpStatusCode.BadRequest);

            var lunchBreak = new[] { new OpeningHourInput(1, "09:00", "12:30"), new OpeningHourInput(1, "14:00", "18:00"), new OpeningHourInput(0, "10:00", "14:00") };
            (await partner.Client.PutAsJsonAsync(url, lunchBreak)).StatusCode.Should().Be(HttpStatusCode.NoContent);
            var saved = (await DetailAsync(partner, id)).OpeningHours;
            saved.Should().HaveCount(3);
            saved[0].DayOfWeek.Should().Be(1); // Monday first, Sunday last

            (await partner.Client.PutAsJsonAsync(url, Array.Empty<OpeningHourInput>())).StatusCode.Should().Be(HttpStatusCode.NoContent);
            (await DetailAsync(partner, id)).OpeningHours.Should().BeEmpty();
        }
        finally
        {
            await DeleteEstablishmentsOfAsync(partner.UserId);
        }
    }

    [Fact]
    public async Task Establishment_input_is_validated()
    {
        using var partner = await TestAccounts.PartnerAsync(api);
        var valid = await InputAsync(UniqueName());

        (await partner.Client.PostAsJsonAsync("/api/v1/pro/establishments", valid with { Name = "A" })).StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await partner.Client.PostAsJsonAsync("/api/v1/pro/establishments", valid with { CustomerServicePhone = "abc" })).StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await partner.Client.PostAsJsonAsync("/api/v1/pro/establishments", valid with { Latitude = 123 })).StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await partner.Client.PostAsJsonAsync("/api/v1/pro/establishments", valid with { CategoryIds = [] })).StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await partner.Client.PostAsJsonAsync("/api/v1/pro/establishments", valid with { CategoryIds = [Guid.NewGuid()] })).StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await partner.Client.PostAsJsonAsync("/api/v1/pro/establishments", valid with { CommuneId = Guid.NewGuid() })).StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await partner.Client.PostAsJsonAsync("/api/v1/pro/establishments", valid with { Email = "pas-un-email" })).StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await partner.Client.GetFromJsonAsync<List<PartnerEstablishmentSummary>>("/api/v1/pro/establishments"))!.Should().BeEmpty();
    }

    [Fact]
    public async Task An_owner_cannot_create_more_than_ten_establishments()
    {
        using var partner = await TestAccounts.PartnerAsync(api);

        try
        {
            for (var i = 0; i < PartnerEstablishmentService.MaxEstablishmentsPerOwner; i++)
            {
                await CreateAsync(partner, await InputAsync(UniqueName()));
            }

            var refused = await partner.Client.PostAsJsonAsync("/api/v1/pro/establishments", await InputAsync(UniqueName()));
            refused.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        }
        finally
        {
            await DeleteEstablishmentsOfAsync(partner.UserId);
        }
    }

    [Fact]
    public async Task Oversized_requests_are_refused_before_being_read()
    {
        using var partner = await TestAccounts.PartnerAsync(api);

        try
        {
            var id = await CreateAsync(partner, await InputAsync(UniqueName()));
            using var huge = TestAccounts.FileForm(new byte[13 * 1024 * 1024]);

            var response = await partner.Client.PostAsync($"/api/v1/pro/establishments/{id}/media", huge);

            response.StatusCode.Should().BeOneOf(HttpStatusCode.RequestEntityTooLarge, HttpStatusCode.BadRequest);
            (await DetailAsync(partner, id)).Media.Should().BeEmpty();
        }
        finally
        {
            await DeleteEstablishmentsOfAsync(partner.UserId);
        }
    }

    [Fact]
    public async Task Establishments_with_the_same_name_get_distinct_slugs()
    {
        using var partner = await TestAccounts.PartnerAsync(api);

        try
        {
            var input = await InputAsync($"Maison Éclat {Guid.NewGuid():N}");
            var first = await DetailAsync(partner, await CreateAsync(partner, input));
            var second = await DetailAsync(partner, await CreateAsync(partner, input));

            first.Slug.Should().StartWith("maison-eclat-").And.NotContain("é");
            second.Slug.Should().Be(first.Slug + "-2");
        }
        finally
        {
            await DeleteEstablishmentsOfAsync(partner.UserId);
        }
    }
}
