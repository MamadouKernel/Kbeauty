using System.Net;
using System.Text.Json;
using FluentAssertions;
using KekeBeauty.Infrastructure.Notifications;
using KekeBeauty.SharedKernel;

namespace KekeBeauty.UnitTests;

public sealed class LeTextoClientTests
{
    private sealed record Call(HttpMethod Method, string Url, string? Authorization, string Body);

    private sealed class StubHandler(HttpStatusCode status, string body) : HttpMessageHandler
    {
        public List<Call> Calls { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Calls.Add(new Call(request.Method, request.RequestUri!.ToString(), request.Headers.Authorization?.ToString(), request.Content is null ? string.Empty : await request.Content.ReadAsStringAsync(cancellationToken)));
            return new HttpResponseMessage(status) { Content = new StringContent(body, System.Text.Encoding.UTF8, "application/json") };
        }
    }

    private static LeTextoOptions Options(bool enabled = true) => new()
    {
        Enabled = enabled,
        ApiKey = "secret-key",
        SmsSender = "KEKE",
        EmailFrom = "noreply@keke.test",
        PublicApiUrl = "https://api.keke.test/",
        WebhookSecret = "dlr secret",
    };

    private static LeTextoClient Client(StubHandler handler) =>
        new(new HttpClient(handler), Microsoft.Extensions.Options.Options.Create(Options()));

    [Fact]
    public async Task A_single_sms_is_posted_with_the_documented_fields_and_the_provider_id_is_kept()
    {
        var handler = new StubHandler(HttpStatusCode.OK, """{"statut":"submitted","id":"aze135dfr-aze135fr","partCount":4}""");

        var result = await Client(handler).SendSmsAsync("05 85 74 33 42", "Bonjour", "https://api.keke.test/dlr?sig=x", CancellationToken.None);

        result.Should().Be(new LeTextoResult(true, "aze135dfr-aze135fr"));
        var call = handler.Calls.Single();
        call.Method.Should().Be(HttpMethod.Post);
        call.Url.Should().Be("https://apis.letexto.com/v1/messages/send");
        call.Authorization.Should().Be("Bearer secret-key");
        using var body = JsonDocument.Parse(call.Body);
        body.RootElement.GetProperty("from").GetString().Should().Be("KEKE");
        body.RootElement.GetProperty("to").GetString().Should().Be("2250585743342", "digits only, country code first, no plus sign");
        body.RootElement.GetProperty("content").GetString().Should().Be("Bonjour");
        body.RootElement.GetProperty("dlrUrl").GetString().Should().Be("https://api.keke.test/dlr?sig=x");
        body.RootElement.GetProperty("dlrMethod").GetString().Should().Be("POST");
    }

    [Fact]
    public async Task Errors_are_read_from_the_documented_shapes_whether_the_message_is_a_list_or_a_string()
    {
        var invalidSender = new StubHandler(HttpStatusCode.BadRequest, """{"statusCode":400,"message":["from must be shorter than or equal to 11 characters"],"errors":"Bad Request"}""");
        var unauthorized = new StubHandler(HttpStatusCode.Unauthorized, """{"statusCode":401,"message":"Invalid username/ password","error":"Unauthorized"}""");
        var garbage = new StubHandler(HttpStatusCode.BadGateway, "<html>Bad gateway</html>");

        (await Client(invalidSender).SendSmsAsync("+2250585743342", "x", null, CancellationToken.None))
            .Should().Match<LeTextoResult>(r => !r.Success && r.Error!.Contains("11 characters", StringComparison.Ordinal));
        (await Client(unauthorized).SendSmsAsync("+2250585743342", "x", null, CancellationToken.None))
            .Should().Match<LeTextoResult>(r => !r.Success && r.Error == "Invalid username/ password");
        (await Client(garbage).SendSmsAsync("+2250585743342", "x", null, CancellationToken.None)).Success.Should().BeFalse();
    }

    [Fact]
    public async Task A_phone_number_that_cannot_be_read_is_refused_without_calling_the_provider()
    {
        var handler = new StubHandler(HttpStatusCode.OK, "{}");

        var result = await Client(handler).SendSmsAsync("not a number", "x", null, CancellationToken.None);

        result.Success.Should().BeFalse();
        handler.Calls.Should().BeEmpty();
    }

    [Fact]
    public async Task An_email_is_sent_as_a_one_recipient_campaign()
    {
        var handler = new StubHandler(HttpStatusCode.OK, """{"id":11,"label":"x","subject":"s","from":"noreply@keke.test","recipientsCount":1,"status":"CREATED"}""");

        var result = await Client(handler).SendEmailAsync("awa@example.com", "Confirmez", "Bonjour", CancellationToken.None);

        result.Should().Be(new LeTextoResult(true, "11"));
        var call = handler.Calls.Single();
        call.Url.Should().Be("https://apis.letexto.com/v1/campaigns/emails");
        using var body = JsonDocument.Parse(call.Body);
        body.RootElement.GetProperty("From").GetString().Should().Be("noreply@keke.test");
        body.RootElement.GetProperty("Recipients").EnumerateArray().Select(e => e.GetString()).Should().Equal("awa@example.com");
        body.RootElement.GetProperty("Subject").GetString().Should().Be("Confirmez");
        body.RootElement.GetProperty("content").GetString().Should().Be("Bonjour");
    }

    [Fact]
    public async Task Senders_balance_and_status_use_the_documented_endpoints()
    {
        var handler = new StubHandler(HttpStatusCode.OK, """{"id":"s1"}""");
        var client = Client(handler);

        await client.CreateSenderAsync("Keke", CancellationToken.None);
        await client.BalanceAsync(CancellationToken.None);
        await client.MessageStatusAsync("abc", CancellationToken.None);

        handler.Calls.Select(c => c.Url).Should().Equal(
            "https://apis.letexto.com/v1/senders/create",
            "https://apis.letexto.com/v1/users/balance?token=secret-key",
            "https://apis.letexto.com/v1/messages/abc/status?token=secret-key");
    }

    [Fact]
    public void The_provider_is_ready_only_when_switched_on_and_fully_configured_and_the_report_url_carries_the_secret()
    {
        Options(enabled: false).SmsReady.Should().BeFalse("configured but disabled");
        Options().SmsReady.Should().BeTrue();
        new LeTextoOptions { Enabled = true, ApiKey = "k", SmsSender = "SenderIsTooLong" }.SmsReady.Should().BeFalse("senders are limited to 11 characters");
        new LeTextoOptions { Enabled = true, SmsSender = "KEKE" }.SmsReady.Should().BeFalse("no API key");
        Options().EmailReady.Should().BeTrue();
        new LeTextoOptions { Enabled = true, ApiKey = "k", EmailFrom = "not-an-address" }.EmailReady.Should().BeFalse();
        Options().DeliveryReportUrl.Should().Be("https://api.keke.test/api/v1/webhooks/sms/letexto?sig=dlr%20secret");
        new LeTextoOptions().DeliveryReportUrl.Should().BeNull();
    }

    [Theory]
    [InlineData("0585743342", "+2250585743342")]
    [InlineData("05 85 74 33 42", "+2250585743342")]
    [InlineData("+225 05 85 74 33 42", "+2250585743342")]
    [InlineData("002250585743342", "+2250585743342")]
    [InlineData("2250585743342", "+2250585743342")]
    [InlineData("+33612345678", "+33612345678")]
    [InlineData("abc", null)]
    [InlineData("", null)]
    [InlineData("+0123", null)]
    public void Phone_numbers_are_normalised_to_e164(string input, string? expected) =>
        PhoneNumbers.ToE164(input).Should().Be(expected);

    [Fact]
    public void Numbers_are_masked_for_logs()
    {
        PhoneNumbers.Mask("+2250585743342").Should().EndWith("3342").And.Contain("*");
        PhoneNumbers.Digits("+2250585743342").Should().Be("2250585743342");
    }
}
