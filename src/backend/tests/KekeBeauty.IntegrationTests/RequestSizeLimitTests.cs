using FluentAssertions;
using Microsoft.AspNetCore.Server.Kestrel.Core;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace KekeBeauty.IntegrationTests;

/// <summary>An anonymous caller must not be able to make the server read megabytes: only the upload routes accept large bodies.</summary>
[Collection(ApiCollection.Name)]
public sealed class RequestSizeLimitTests(ApiFixture api)
{
    [Fact]
    public void The_server_wide_request_body_limit_is_small_and_only_the_upload_routes_raise_it()
    {
        // The in-memory test server does not enforce the limit (the real stack does, see the audit report): this guards the setting itself.
        var limit = api.Services.GetRequiredService<IOptions<KestrelServerOptions>>().Value.Limits.MaxRequestBodySize;

        limit.Should().BeLessThanOrEqualTo(1024 * 1024, "JSON requests are a few kilobytes; uploads set their own limit");
    }

    [Fact]
    public void The_four_upload_routes_carry_their_own_limit_so_the_small_global_limit_does_not_break_them()
    {
        var routes = api.Services.GetRequiredService<Microsoft.AspNetCore.Routing.EndpointDataSource>().Endpoints
            .OfType<Microsoft.AspNetCore.Routing.RouteEndpoint>()
            .Where(endpoint => endpoint.Metadata.GetMetadata<Microsoft.AspNetCore.Http.Metadata.IAcceptsMetadata>() is { } accepts
                && accepts.ContentTypes.Contains("multipart/form-data"))
            .ToList();

        routes.Should().HaveCountGreaterThanOrEqualTo(4);
        routes.Should().OnlyContain(endpoint => endpoint.Metadata.GetMetadata<Microsoft.AspNetCore.Http.Metadata.IRequestSizeLimitMetadata>() != null,
            "every file upload must set its own request size limit, otherwise the global limit would refuse it");
    }
}
