using System.Net;
using System.Text.Json;
using FluentAssertions;
using KekeBeauty.Infrastructure.Billing;
using KekeBeauty.SharedKernel;
using Microsoft.Extensions.Options;

namespace KekeBeauty.UnitTests;

public sealed class CinetPayGatewayTests
{
    private sealed class StubHandler(Func<string, string, (HttpStatusCode Status, string Body)> respond) : HttpMessageHandler
    {
        public List<(string Url, string Body)> Calls { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var body = request.Content is null ? string.Empty : await request.Content.ReadAsStringAsync(cancellationToken);
            Calls.Add((request.RequestUri!.ToString(), body));
            var (status, text) = respond(request.RequestUri!.AbsolutePath, body);
            return new HttpResponseMessage(status) { Content = new StringContent(text, System.Text.Encoding.UTF8, "application/json") };
        }
    }

    private static CinetPayGateway Gateway(StubHandler handler, string secret = "sandbox-secret") =>
        new(
            new HttpClient(handler),
            Options.Create(new PaymentsOptions
            {
                Provider = "CinetPay",
                CinetPay = new CinetPayOptions { ApiKey = "api-key", SiteId = "105000", SecretKey = secret, BaseUrl = "https://sandbox.test/v2" },
            }));

    private static PaymentRequest Request(decimal amount = 15000) =>
        new("KBABC123", amount, "XOF", "Abonnement Premium — Salon_A & B (KB-2026-000001)", "Awa Koné", "awa@example.com", "+22507000000",
            "https://keke.test/retour", "https://keke.test/api/v1/webhooks/payments/cinetpay");

    [Fact]
    public async Task Create_posts_the_documented_fields_and_returns_the_payment_page()
    {
        var handler = new StubHandler((_, _) => (HttpStatusCode.OK, """{"code":"201","message":"CREATED","data":{"payment_token":"tok","payment_url":"https://checkout.test/pay/tok"}}"""));

        var session = await Gateway(handler).CreateAsync(Request());

        session.PaymentUrl.Should().Be("https://checkout.test/pay/tok");
        var (url, body) = handler.Calls.Single();
        url.Should().Be("https://sandbox.test/v2/payment");
        using var json = JsonDocument.Parse(body);
        var root = json.RootElement;
        root.GetProperty("apikey").GetString().Should().Be("api-key");
        root.GetProperty("site_id").GetString().Should().Be("105000");
        root.GetProperty("transaction_id").GetString().Should().Be("KBABC123");
        root.GetProperty("amount").GetInt64().Should().Be(15000);
        root.GetProperty("currency").GetString().Should().Be("XOF");
        root.GetProperty("notify_url").GetString().Should().EndWith("/webhooks/payments/cinetpay");
        root.GetProperty("channels").GetString().Should().Be("ALL");
        root.GetProperty("description").GetString().Should().NotContainAny("_", "&", "#", "/", "$");
    }

    [Theory]
    [InlineData(12)]
    [InlineData(95)]
    [InlineData(100.5)]
    public async Task Create_refuses_amounts_the_provider_would_refuse_without_calling_it(double amount)
    {
        var handler = new StubHandler((_, _) => (HttpStatusCode.OK, "{}"));

        var act = () => Gateway(handler).CreateAsync(Request((decimal)amount));

        await act.Should().ThrowAsync<PaymentGatewayException>();
        handler.Calls.Should().BeEmpty();
    }

    [Fact]
    public async Task Create_reports_a_provider_refusal()
    {
        var handler = new StubHandler((_, _) => (HttpStatusCode.BadRequest, """{"code":"-1","message":"INVALID_PARAMS"}"""));

        var act = () => Gateway(handler).CreateAsync(Request());

        (await act.Should().ThrowAsync<PaymentGatewayException>()).Which.Message.Should().Contain("INVALID_PARAMS");
    }

    [Fact]
    public async Task Verify_reads_an_accepted_payment_with_its_amount_as_text()
    {
        var handler = new StubHandler((_, _) => (HttpStatusCode.OK,
            """{"code":"00","message":"SUCCES","data":{"amount":"15000","currency":"XOF","status":"ACCEPTED","payment_method":"OM","operator_id":"x"}}"""));

        var result = await Gateway(handler).VerifyAsync("KBABC123");

        result.Status.Should().Be(GatewayStatus.Accepted);
        result.Amount.Should().Be(15000);
        result.Currency.Should().Be("XOF");
        result.Method.Should().Be("OM");
        handler.Calls.Single().Url.Should().Be("https://sandbox.test/v2/payment/check");
        handler.Calls.Single().Body.Should().Contain("KBABC123");
    }

    [Fact]
    public async Task Verify_reads_a_refused_payment()
    {
        var handler = new StubHandler((_, _) => (HttpStatusCode.OK,
            """{"code":"627","message":"TRANSACTION_CANCEL","data":{"amount":"15000","currency":"XOF","status":"REFUSED","payment_method":"MOMO"}}"""));

        (await Gateway(handler).VerifyAsync("KBABC123")).Status.Should().Be(GatewayStatus.Refused);
    }

    [Theory]
    [InlineData("""{"code":"625","message":"TRANSACTION_NOT_FOUND"}""")]
    [InlineData("""{"code":"662","message":"WAITING_CUSTOMER_PAYMENT","data":{"status":"WAITING_FOR_CUSTOMER","amount":"15000","currency":"XOF"}}""")]
    public async Task Verify_treats_unknown_or_waiting_transactions_as_pending(string body)
    {
        var handler = new StubHandler((_, _) => (HttpStatusCode.OK, body));

        (await Gateway(handler).VerifyAsync("KBABC123")).Status.Should().Be(GatewayStatus.Pending);
    }

    [Fact]
    public async Task Verify_turns_an_unreadable_answer_into_a_gateway_error()
    {
        var handler = new StubHandler((_, _) => (HttpStatusCode.BadGateway, "<html>Bad gateway</html>"));

        var act = () => Gateway(handler).VerifyAsync("KBABC123");

        await act.Should().ThrowAsync<PaymentGatewayException>();
    }

    [Fact]
    public void Notifications_are_authentic_only_with_the_right_signature()
    {
        var gateway = Gateway(new StubHandler((_, _) => (HttpStatusCode.OK, "{}")));
        var fields = new Dictionary<string, string>
        {
            ["cpm_site_id"] = "105000",
            ["cpm_trans_id"] = "KBABC123",
            ["cpm_trans_date"] = "2026-10-12 10:00:00",
            ["cpm_amount"] = "15000",
            ["cpm_currency"] = "XOF",
            ["signature"] = "sig",
            ["payment_method"] = "OM",
            ["cel_phone_num"] = "0700000000",
            ["cpm_phone_prefixe"] = "225",
            ["cpm_language"] = "fr",
            ["cpm_version"] = "V4",
            ["cpm_payment_config"] = "SINGLE",
            ["cpm_page_action"] = "PAYMENT",
            ["cpm_custom"] = string.Empty,
            ["cpm_designation"] = "Abonnement",
            ["cpm_error_message"] = "SUCCES",
        };
        var token = CinetPayGateway.ComputeToken("sandbox-secret", fields);

        gateway.IsNotificationAuthentic(fields, token).Should().BeTrue();
        gateway.IsNotificationAuthentic(fields, token.ToUpperInvariant()).Should().BeTrue("hex case does not matter");
        gateway.IsNotificationAuthentic(fields, null).Should().BeFalse();
        gateway.IsNotificationAuthentic(fields, "deadbeef").Should().BeFalse();
        gateway.IsNotificationAuthentic(new Dictionary<string, string>(fields) { ["cpm_amount"] = "1" }, token).Should().BeFalse("a tampered amount breaks the signature");
        Gateway(new StubHandler((_, _) => (HttpStatusCode.OK, "{}")), secret: string.Empty).IsNotificationAuthentic(fields, token).Should().BeFalse("no secret, no trust");
    }

    [Theory]
    [InlineData("Salon_A & B/C#1$", "Salon A B C 1")]
    [InlineData("  Spa   Ébène  ", "Spa Ébène")]
    public void Free_text_is_cleaned_of_the_characters_the_provider_dislikes(string input, string expected) =>
        CinetPayGateway.Clean(input).Should().Be(expected);

    [Theory]
    [InlineData(15000, true)]
    [InlineData(100, true)]
    [InlineData(95, false)]
    [InlineData(15001, false)]
    public void Amounts_must_be_multiples_of_five_of_at_least_one_hundred(int amount, bool accepted) =>
        CinetPayGateway.IsAcceptedAmount(amount).Should().Be(accepted);
}
