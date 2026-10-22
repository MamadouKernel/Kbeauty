using System.Net;
using System.Security.Cryptography;
using System.Text;
using FluentAssertions;
using KekeBeauty.Infrastructure.Notifications;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace KekeBeauty.UnitTests;

public sealed class ZavuSignatureTests
{
    private const string Secret = "whsec_test_secret_value";
    private static readonly DateTimeOffset Now = new(2026, 10, 22, 12, 0, 0, TimeSpan.Zero);
    private static readonly byte[] Body = Encoding.UTF8.GetBytes("""{"id":"evt_1","type":"message.delivered","data":{"messageId":"m1"}}""");

    private static string Hmac(string text) => Convert.ToHexStringLower(HMACSHA256.HashData(Encoding.UTF8.GetBytes(Secret), Encoding.UTF8.GetBytes(text)));

    private static string V2(DateTimeOffset at) => $"t={at.ToUnixTimeSeconds()},v2={Hmac($"{at.ToUnixTimeSeconds()}." + Encoding.UTF8.GetString(Body))}";

    [Fact]
    public void A_correct_v2_signature_is_accepted()
    {
        ZavuSignature.IsValid(Body, V2(Now), Secret, Now).Should().BeTrue();
    }

    [Fact]
    public void A_correct_v1_signature_signs_the_body_alone()
    {
        var header = $"t={Now.ToUnixTimeSeconds()},v1={Hmac(Encoding.UTF8.GetString(Body))}";

        ZavuSignature.IsValid(Body, header, Secret, Now).Should().BeTrue();
    }

    [Fact]
    public void A_v1_signature_made_the_v2_way_is_refused()
    {
        var header = $"t={Now.ToUnixTimeSeconds()},v1={Hmac($"{Now.ToUnixTimeSeconds()}." + Encoding.UTF8.GetString(Body))}";

        ZavuSignature.IsValid(Body, header, Secret, Now).Should().BeFalse();
    }

    [Fact]
    public void A_tampered_body_the_wrong_secret_or_a_missing_header_are_refused()
    {
        var tampered = Encoding.UTF8.GetBytes("""{"id":"evt_1","type":"message.failed","data":{"messageId":"m1"}}""");

        ZavuSignature.IsValid(tampered, V2(Now), Secret, Now).Should().BeFalse();
        ZavuSignature.IsValid(Body, V2(Now), "another-secret", Now).Should().BeFalse();
        ZavuSignature.IsValid(Body, V2(Now), string.Empty, Now).Should().BeFalse();
        ZavuSignature.IsValid(Body, null, Secret, Now).Should().BeFalse();
        ZavuSignature.IsValid(Body, string.Empty, Secret, Now).Should().BeFalse();
        ZavuSignature.IsValid(Body, "garbage", Secret, Now).Should().BeFalse();
        ZavuSignature.IsValid(Body, $"t={Now.ToUnixTimeSeconds()}", Secret, Now).Should().BeFalse();
    }

    [Fact]
    public void A_replayed_old_call_or_one_from_the_future_is_refused()
    {
        ZavuSignature.IsValid(Body, V2(Now.AddSeconds(-299)), Secret, Now).Should().BeTrue();
        ZavuSignature.IsValid(Body, V2(Now.AddSeconds(-301)), Secret, Now).Should().BeFalse();
        ZavuSignature.IsValid(Body, V2(Now.AddSeconds(59)), Secret, Now).Should().BeTrue();
        ZavuSignature.IsValid(Body, V2(Now.AddSeconds(61)), Secret, Now).Should().BeFalse();
    }

    [Fact]
    public void A_milliseconds_timestamp_is_refused()
    {
        var ms = Now.ToUnixTimeMilliseconds();
        var header = $"t={ms},v2={Hmac($"{ms}." + Encoding.UTF8.GetString(Body))}";

        ZavuSignature.IsValid(Body, header, Secret, Now).Should().BeFalse();
    }
}

public sealed class ZavuWhatsAppSenderTests
{
    private sealed class Handler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
    {
        public List<(HttpRequestMessage Request, string Body)> Calls { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Calls.Add((request, request.Content is null ? string.Empty : await request.Content.ReadAsStringAsync(cancellationToken)));
            return respond(request);
        }
    }

    private static HttpResponseMessage Json(string body, HttpStatusCode status = HttpStatusCode.OK) =>
        new(status) { Content = new StringContent(body, Encoding.UTF8, "application/json") };

    private const string Templates = """
        {"items":[
          {"id":"tpl_draft","name":"keke_notification","language":"fr","status":"draft"},
          {"id":"tpl_otp","name":"keke_otp","language":"fr","status":"approved"},
          {"id":"tpl_notif","name":"keke_notification","language":"fr","status":"approved"}
        ],"nextCursor":null}
        """;

    private static (ZavuWhatsAppSender Sender, Handler Handler) Make(Func<HttpRequestMessage, HttpResponseMessage>? send = null, string apiKey = "zv_test_key")
    {
        var handler = new Handler(request => request.RequestUri!.AbsolutePath switch
        {
            "/v1/templates" => Json(Templates),
            "/v1/messages" => send?.Invoke(request) ?? Json("""{"message":{"id":"msg_42","status":"queued"}}""", HttpStatusCode.Accepted),
            _ => new HttpResponseMessage(HttpStatusCode.NotFound),
        });
        var settings = new ZavuOptions { ApiKey = apiKey, BaseUrl = "https://zavu.test", SenderId = "snd_1" };
        var client = new HttpClient(handler);
        ZavuWhatsAppSender.Configure(client, settings);
        return (new ZavuWhatsAppSender(client, new MemoryCache(new MemoryCacheOptions()), Options.Create(settings), NullLogger<ZavuWhatsAppSender>.Instance), handler);
    }

    [Fact]
    public async Task It_sends_an_approved_template_with_positional_variables_and_the_bearer_key()
    {
        var (sender, handler) = Make();

        var result = await sender.SendTemplateAsync("+2250700001234", "keke_otp", "fr", ["123456"], true);

        result.Should().BeEquivalentTo(new KekeBeauty.SharedKernel.WhatsAppResult(true, "msg_42"));
        var send = handler.Calls.Single(c => c.Request.RequestUri!.AbsolutePath == "/v1/messages");
        send.Request.Headers.Authorization!.ToString().Should().Be("Bearer zv_test_key");
        send.Request.Headers.GetValues("Zavu-Sender").Should().Equal("snd_1");
        send.Body.Should().Contain("\"templateId\":\"tpl_otp\"").And.Contain("\"1\":\"123456\"")
            .And.Contain("\"channel\":\"whatsapp\"").And.Contain("\"messageType\":\"template\"").And.Contain("\"fallbackEnabled\":false");
    }

    [Fact]
    public async Task It_uses_the_approved_template_not_a_draft_with_the_same_name_and_remembers_the_id()
    {
        var (sender, handler) = Make();

        await sender.SendTemplateAsync("+2250700001234", "keke_notification", "fr", ["Titre", "Message"], false);
        await sender.SendTemplateAsync("+2250700001234", "keke_notification", "fr", ["Titre", "Message"], false);

        handler.Calls.Where(c => c.Request.RequestUri!.AbsolutePath == "/v1/templates").Should().ContainSingle();
        handler.Calls.Where(c => c.Request.RequestUri!.AbsolutePath == "/v1/messages").Should().OnlyContain(c => c.Body.Contains("tpl_notif"));
    }

    [Fact]
    public async Task A_template_that_is_not_approved_fails_visibly_without_sending()
    {
        var (sender, handler) = Make();

        var result = await sender.SendTemplateAsync("+2250700001234", "keke_unknown", "fr", ["x"], false);

        result.Success.Should().BeFalse();
        result.Error.Should().Contain("keke_unknown");
        handler.Calls.Should().NotContain(c => c.Request.RequestUri!.AbsolutePath == "/v1/messages");
    }

    [Theory]
    [InlineData("a\nb", "a · b")]
    [InlineData("a\r\nb\tc", "a · b · c")]
    [InlineData("a      b", "a · b")]
    [InlineData("  ok  ", "ok")]
    public void Values_never_carry_line_breaks_tabs_or_long_runs_of_spaces(string input, string expected)
    {
        ZavuWhatsAppSender.Clean(input).Should().Be(expected);
    }

    [Fact]
    public async Task Newlines_in_a_notification_are_cleaned_before_they_reach_zavu()
    {
        var (sender, handler) = Make();

        await sender.SendTemplateAsync("+2250700001234", "keke_notification", "fr", ["Titre", "ligne 1\nligne 2"], false);

        handler.Calls.Single(c => c.Request.RequestUri!.AbsolutePath == "/v1/messages").Body.Should().NotContain("\\n").And.Contain("ligne 1 \\u00B7 ligne 2");
    }

    [Theory]
    [InlineData(HttpStatusCode.TooManyRequests, """{"error":{"code":"a2p_limit_exceeded","message":"limit"}}""", "quota mensuel")]
    [InlineData(HttpStatusCode.TooManyRequests, """{"code":"daily_limit_exceeded","message":"limit"}""", "plafond quotidien")]
    [InlineData(HttpStatusCode.BadRequest, """{"code":"whatsapp_window_closed","message":"closed"}""", "24 h")]
    [InlineData(HttpStatusCode.Forbidden, """{"code":"sending_suspended","message":"suspended"}""", "suspendu")]
    [InlineData(HttpStatusCode.Unauthorized, """{"code":"unauthorized","message":"no"}""", "clé")]
    public async Task A_refusal_becomes_a_readable_failure_never_an_exception(HttpStatusCode status, string body, string expected)
    {
        var (sender, _) = Make(_ => Json(body, status));

        var result = await sender.SendTemplateAsync("+2250700001234", "keke_otp", "fr", ["1"], true);

        result.Success.Should().BeFalse();
        result.Error.Should().Contain(expected);
        result.Error.Should().NotContain("zv_");
    }

    [Fact]
    public async Task An_unreachable_provider_is_a_failure_not_an_exception()
    {
        var (sender, _) = Make(_ => throw new HttpRequestException("down"));

        var result = await sender.SendTemplateAsync("+2250700001234", "keke_otp", "fr", ["1"], true);

        result.Success.Should().BeFalse();
        result.Error.Should().Contain("injoignable");
    }

    [Fact]
    public async Task Without_an_api_key_nothing_is_called()
    {
        var (sender, handler) = Make(apiKey: string.Empty);

        var result = await sender.SendTemplateAsync("+2250700001234", "keke_otp", "fr", ["1"], true);

        result.Success.Should().BeFalse();
        handler.Calls.Should().BeEmpty();
    }
}
