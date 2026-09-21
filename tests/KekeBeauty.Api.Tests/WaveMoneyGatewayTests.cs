using System.Net;
using System.Text;
using KekeBeauty.Infrastructure.Billing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;

namespace KekeBeauty.Api.Tests;

public sealed class WaveMoneyGatewayTests
{
    [Fact]
    public async Task Refund_WithoutApiKey_FailsExplicitly()
    {
        var gateway = Create(new CaptureHandler(_ => new(HttpStatusCode.OK)), new Dictionary<string, string?>());
        var result = await gateway.RefundCheckoutAsync("cos-test", CancellationToken.None);
        Assert.False(result.Success);
        Assert.Equal("wave_not_configured", result.Status);
    }

    [Fact]
    public async Task Refund_UsesOfficialEndpointAndBearerToken()
    {
        HttpRequestMessage? captured = null;
        var handler = new CaptureHandler(request => { captured = request; return new(HttpStatusCode.OK); });
        var gateway = Create(handler, Settings());
        var result = await gateway.RefundCheckoutAsync("cos-123", CancellationToken.None);
        Assert.True(result.Success);
        Assert.Equal("https://api.wave.com/v1/checkout/sessions/cos-123/refund", captured!.RequestUri!.ToString());
        Assert.Equal("Bearer", captured.Headers.Authorization!.Scheme);
        Assert.Equal("secret", captured.Headers.Authorization.Parameter);
    }

    [Fact]
    public async Task Payout_SendsIdempotencyAndExpectedPayload()
    {
        HttpRequestMessage? captured = null;
        var handler = new CaptureHandler(request =>
        {
            captured = request;
            return new(HttpStatusCode.OK) { Content = new StringContent("{\"id\":\"pt-1\",\"status\":\"succeeded\"}") };
        });
        var result = await Create(handler, Settings()).CreatePayoutAsync("+2250102030405", "Awa", 1500, "tip-1", "idem-1", CancellationToken.None);
        Assert.True(result.Success);
        Assert.Equal("pt-1", result.Reference);
        Assert.Equal("idem-1", captured!.Headers.GetValues("idempotency-key").Single());
        Assert.Contains("\"currency\":\"XOF\"", handler.Body);
        Assert.Contains("\"receive_amount\":\"1500\"", handler.Body);
    }

    private static Dictionary<string, string?> Settings() => new() { ["Payments:Wave:ApiKey"] = "secret" };
    private static WaveMoneyGateway Create(HttpMessageHandler handler, IDictionary<string, string?> settings)
    {
        var config = new ConfigurationBuilder().AddInMemoryCollection(settings).Build();
        return new(new HttpClient(handler) { BaseAddress = new Uri("https://api.wave.com/") }, config, NullLogger<WaveMoneyGateway>.Instance);
    }

    private sealed class CaptureHandler(Func<HttpRequestMessage, HttpResponseMessage> callback) : HttpMessageHandler
    {
        public string? Body { get; private set; }
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            if (request.Content is not null) Body = await request.Content.ReadAsStringAsync(cancellationToken);
            return callback(request);
        }
    }
}
