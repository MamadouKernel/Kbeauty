using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using KekeBeauty.Infrastructure.Partner;

namespace KekeBeauty.IntegrationTests;

[Collection(ApiCollection.Name)]
public sealed class VideoTests(ApiFixture api)
{
    private static byte[] Mp4(int totalBytes = 4096)
    {
        var bytes = new byte[totalBytes];
        bytes[3] = 0x18;
        "ftypisom"u8.CopyTo(bytes.AsSpan(4));
        for (var i = 16; i < bytes.Length; i++)
        {
            bytes[i] = (byte)(i % 251);
        }

        return bytes;
    }

    private static MultipartFormDataContent VideoForm(byte[] video, byte[]? poster = null, int? duration = null)
    {
        var form = TestAccounts.FileForm(video, "clip.mp4", "video/mp4");
        if (poster is not null)
        {
            var image = new ByteArrayContent(poster);
            image.Headers.ContentType = new MediaTypeHeaderValue("image/png");
            form.Add(image, "poster", "poster.png");
        }

        if (duration is not null)
        {
            form.Add(new StringContent(duration.Value.ToString()), "durationSeconds");
        }

        return form;
    }

    private static string Videos(Salon salon) => $"/api/v1/pro/establishments/{salon.Id}/videos";

    private static string PathOf(string url) => Uri.TryCreate(url, UriKind.Absolute, out var absolute) ? absolute.PathAndQuery : url;

    private async Task<JsonElement> PublicMediaAsync(Salon salon)
    {
        using var anonymous = api.CreateClient();
        var json = await anonymous.GetStringAsync($"/api/v1/establishments/{salon.Slug}");
        return JsonDocument.Parse(json).RootElement.GetProperty("media").Clone();
    }

    [Fact]
    public async Task Premium_partner_uploads_a_video_that_is_listed_after_the_photos_and_streams_with_ranges()
    {
        using var admin = await TestAccounts.AdminAsync(api);
        var salon = await TestSalons.PublishAsync(api, admin, subscribe: true);

        try
        {
            var bytes = Mp4();
            using var form = VideoForm(bytes, TestAccounts.Png(640, 360), 42);
            var response = await salon.Owner.Client.PostAsync(Videos(salon), form);
            response.StatusCode.Should().Be(HttpStatusCode.Created);
            var item = (await response.Content.ReadFromJsonAsync<PartnerMediaItem>())!;
            item.Kind.Should().Be("Video");
            item.Url.Should().EndWith(".mp4");
            item.PosterUrl.Should().Contain("_poster");
            item.DurationSeconds.Should().Be(42);
            item.IsCover.Should().BeFalse();

            var media = await PublicMediaAsync(salon);
            media.EnumerateArray().Select(m => m.GetProperty("kind").GetString()).Should().Equal("Photo", "Video");

            using var anonymous = api.CreateClient();
            var whole = await anonymous.GetAsync(PathOf(item.Url));
            whole.StatusCode.Should().Be(HttpStatusCode.OK);
            whole.Content.Headers.ContentType!.MediaType.Should().Be("video/mp4");
            whole.Headers.AcceptRanges.Should().Contain("bytes");
            (await whole.Content.ReadAsByteArrayAsync()).Should().Equal(bytes);

            using var request = new HttpRequestMessage(HttpMethod.Get, PathOf(item.Url));
            request.Headers.Range = new RangeHeaderValue(100, 199);
            var partial = await anonymous.SendAsync(request);
            partial.StatusCode.Should().Be(HttpStatusCode.PartialContent);
            partial.Content.Headers.ContentRange!.ToString().Should().Be($"bytes 100-199/{bytes.Length}");
            (await partial.Content.ReadAsByteArrayAsync()).Should().Equal(bytes.AsSpan(100, 100).ToArray());

            (await salon.Owner.Client.DeleteAsync($"/api/v1/pro/establishments/{salon.Id}/media/{item.Id}")).StatusCode.Should().Be(HttpStatusCode.NoContent);
            (await PublicMediaAsync(salon)).EnumerateArray().Should().ContainSingle();
        }
        finally
        {
            await TestCleanup.DeleteEstablishmentsAsync(api, salon.Owner.UserId);
        }
    }

    [Fact]
    public async Task Videos_are_refused_on_the_free_plan_when_not_a_video_and_beyond_the_limit()
    {
        using var admin = await TestAccounts.AdminAsync(api);
        var free = await TestSalons.PublishAsync(api, admin, subscribe: false);
        var premium = await TestSalons.PublishAsync(api, admin, subscribe: true);

        try
        {
            using var onFree = VideoForm(Mp4());
            (await free.Owner.Client.PostAsync(Videos(free), onFree)).StatusCode.Should().Be(HttpStatusCode.PaymentRequired);

            using var disguised = VideoForm("<script>alert(1)</script>"u8.ToArray());
            (await premium.Owner.Client.PostAsync(Videos(premium), disguised)).StatusCode.Should().Be(HttpStatusCode.BadRequest);

            using var heic = VideoForm("\0\0\0\u0018ftypheic\0\0\0\0"u8.ToArray());
            (await premium.Owner.Client.PostAsync(Videos(premium), heic)).StatusCode.Should().Be(HttpStatusCode.BadRequest);

            for (var i = 0; i < 3; i++)
            {
                using var ok = VideoForm(Mp4(2048 + i));
                (await premium.Owner.Client.PostAsync(Videos(premium), ok)).StatusCode.Should().Be(HttpStatusCode.Created);
            }

            using var fourth = VideoForm(Mp4());
            (await premium.Owner.Client.PostAsync(Videos(premium), fourth)).StatusCode.Should().Be(HttpStatusCode.Conflict);

            using var stranger = await TestAccounts.PartnerAsync(api);
            using var other = VideoForm(Mp4());
            (await stranger.Client.PostAsync(Videos(premium), other)).StatusCode.Should().Be(HttpStatusCode.NotFound);
            await TestCleanup.DeleteEstablishmentsAsync(api, stranger.UserId);
        }
        finally
        {
            await TestCleanup.DeleteEstablishmentsAsync(api, free.Owner.UserId);
            await TestCleanup.DeleteEstablishmentsAsync(api, premium.Owner.UserId);
        }
    }
}
