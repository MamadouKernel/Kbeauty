using System.Net;
using FluentAssertions;
using KekeBeauty.Infrastructure.Billing;
using KekeBeauty.Modules.Billing.Domain;
using KekeBeauty.SharedKernel;
using Microsoft.Extensions.Options;

namespace KekeBeauty.UnitTests;

public sealed class WinipayGatewayTests
{
    private sealed record Call(string Url, string Body, string? Apply, string? Token);

    private sealed class StubHandler(Func<string, (HttpStatusCode Status, string Body)> respond) : HttpMessageHandler
    {
        public List<Call> Calls { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var body = request.Content is null ? string.Empty : await request.Content.ReadAsStringAsync(cancellationToken);
            Calls.Add(new Call(
                request.RequestUri!.ToString(),
                body,
                request.Headers.TryGetValues("X-Merchant-Apply", out var apply) ? apply.Single() : null,
                request.Headers.TryGetValues("X-Merchant-Token", out var token) ? token.Single() : null));
            var (status, text) = respond(request.RequestUri!.AbsolutePath);
            return new HttpResponseMessage(status) { Content = new StringContent(text, System.Text.Encoding.UTF8, "application/json") };
        }
    }

    private static WinipayGateway Gateway(StubHandler handler, string token = "merchant-token") =>
        new(
            new HttpClient(handler),
            Options.Create(new PaymentsOptions
            {
                Provider = "Winipay",
                Winipay = new WinipayOptions { ApplyKey = "apply-key", Token = token, Env = "test", BaseUrl = "https://api.winipayer.test" },
            }));

    private static PaymentRequest Request(decimal amount = 15000) =>
        new("KBABC123", amount, "XOF", "Abonnement Premium — Salon A (KB-2026-000001)", "Awa Koné", "awa@example.com", "+22507000000",
            "https://keke.test/pro/etablissements/e1/abonnement?tx=KBABC123", "https://keke.test/api/v1/webhooks/payments/winipay");

    private const string Created = """{"success":true,"results":{"uuid":"2f1e1d74-78f3","crypto":"invoice_test_x","env":"test","amount":15000,"currency":"xof","checkout_process":"https://checkout.winipayer.test/process/tng/invoice_test_x","expired_at":"2026-10-12 11:00:00"},"errors":[],"messages":[]}""";

    private static string Detail(string state, string amountInit = "15000", string amount = "15000", string currency = "xof", string op = "wave-cote-divoire") =>
        $$$"""{"success":true,"results":{"invoice":{"uuid":"2f1e1d74-78f3","state":"{{{state}}}","amount_init":{{{amountInit}}},"amount":{{{amount}}},"currency":"{{{currency}}}","operator":"{{{op}}}","operator_ref":"TBL1"}},"errors":[],"messages":[]}""";

    [Fact]
    public async Task Create_sends_the_documented_headers_and_fields_and_keeps_the_invoice_uuid()
    {
        var handler = new StubHandler(_ => (HttpStatusCode.OK, Created));

        var session = await Gateway(handler).CreateAsync(Request());

        session.PaymentUrl.Should().Be("https://checkout.winipayer.test/process/tng/invoice_test_x");
        session.ProviderReference.Should().Be("2f1e1d74-78f3");
        var call = handler.Calls.Single();
        call.Url.Should().Be("https://api.winipayer.test/checkout/standard/create");
        call.Apply.Should().Be("apply-key");
        call.Token.Should().Be("merchant-token");
        foreach (var expected in new[] { "name=env", "test", "name=amount", "15000", "name=description", "name=return_url", "name=cancel_url", "cancelled=1", "name=callback_url", "tx=KBABC123&sig=" })
        {
            call.Body.Should().Contain(expected);
        }

        call.Body.Should().NotContain("\n\n\n");
    }

    [Fact]
    public async Task Create_puts_a_signature_in_the_callback_url_that_the_gateway_recognises()
    {
        var handler = new StubHandler(_ => (HttpStatusCode.OK, Created));
        var gateway = Gateway(handler);

        await gateway.CreateAsync(Request());

        var body = handler.Calls.Single().Body;
        var signature = body[(body.IndexOf("sig=", StringComparison.Ordinal) + 4)..].Split('\r', '\n')[0];
        var fields = new Dictionary<string, string> { ["tx"] = "KBABC123" };
        gateway.IsNotificationAuthentic(fields, signature).Should().BeTrue();
        gateway.IsNotificationAuthentic(fields, "00ff").Should().BeFalse();
        gateway.IsNotificationAuthentic(new Dictionary<string, string> { ["tx"] = "KBOTHER" }, signature).Should().BeFalse("the signature is bound to the transaction");
        gateway.IsNotificationAuthentic(fields, null).Should().BeFalse();
        Gateway(handler, token: string.Empty).IsNotificationAuthentic(fields, signature).Should().BeFalse();
        gateway.TransactionIdFrom(fields).Should().Be("KBABC123");
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-5)]
    [InlineData(1500.5)]
    public async Task Create_refuses_amounts_that_are_not_whole_and_positive(double amount)
    {
        var handler = new StubHandler(_ => (HttpStatusCode.OK, Created));

        var act = () => Gateway(handler).CreateAsync(Request((decimal)amount));

        await act.Should().ThrowAsync<PaymentGatewayException>();
        handler.Calls.Should().BeEmpty();
    }

    [Fact]
    public async Task Create_reports_the_providers_error()
    {
        var handler = new StubHandler(_ => (HttpStatusCode.UnprocessableEntity,
            """{"success":false,"results":[],"errors":{"code":2000,"key":"X-Merchant-Token","msg":"Invalid or missing X-Merchant-Token"},"messages":[]}"""));

        var act = () => Gateway(handler).CreateAsync(Request());

        (await act.Should().ThrowAsync<PaymentGatewayException>()).Which.Message.Should().Contain("X-Merchant-Token");
    }

    [Fact]
    public async Task Verify_reads_the_invoice_by_its_uuid_and_accepts_a_success_state()
    {
        var handler = new StubHandler(_ => (HttpStatusCode.OK, Detail("success")));

        var result = await Gateway(handler).VerifyAsync("KBABC123", "2f1e1d74-78f3");

        result.Status.Should().Be(GatewayStatus.Accepted);
        result.Amount.Should().Be(15000);
        result.Currency.Should().Be("XOF");
        result.Method.Should().Be("wave-cote-divoire");
        handler.Calls.Single().Url.Should().Be("https://api.winipayer.test/checkout/standard/detail/2f1e1d74-78f3");
        handler.Calls.Single().Body.Should().Contain("name=env");
    }

    [Fact]
    public async Task Verify_uses_the_requested_amount_not_the_one_with_fees()
    {
        var handler = new StubHandler(_ => (HttpStatusCode.OK, Detail("success", amountInit: "15000", amount: "15375")));

        (await Gateway(handler).VerifyAsync("KBABC123", "uuid")).Amount.Should().Be(15000);
    }

    [Theory]
    [InlineData("failed", GatewayStatus.Refused)]
    [InlineData("cancelled", GatewayStatus.Refused)]
    [InlineData("expired", GatewayStatus.Refused)]
    [InlineData("pending", GatewayStatus.Pending)]
    [InlineData("processing", GatewayStatus.Pending)]
    [InlineData("something-new", GatewayStatus.Pending)]
    public async Task Verify_only_trusts_success_and_never_invents_a_payment(string state, GatewayStatus expected)
    {
        var handler = new StubHandler(_ => (HttpStatusCode.OK, Detail(state)));

        (await Gateway(handler).VerifyAsync("KBABC123", "uuid")).Status.Should().Be(expected);
    }

    [Fact]
    public async Task Verify_treats_an_unknown_invoice_or_a_missing_reference_as_not_paid()
    {
        var unknown = new StubHandler(_ => (HttpStatusCode.OK, """{"success":false,"results":[],"errors":{"code":3000,"key":"uuid","msg":"Invalide or missing invoice"},"messages":[]}"""));
        (await Gateway(unknown).VerifyAsync("KBABC123", "gone")).Status.Should().Be(GatewayStatus.Pending);

        var none = new StubHandler(_ => (HttpStatusCode.OK, "{}"));
        (await Gateway(none).VerifyAsync("KBABC123", null)).Status.Should().Be(GatewayStatus.Pending);
        none.Calls.Should().BeEmpty("without a provider reference there is nothing to ask");
    }

    [Fact]
    public async Task Verify_turns_an_unreadable_answer_into_a_gateway_error()
    {
        var handler = new StubHandler(_ => (HttpStatusCode.BadGateway, "<html>Bad gateway</html>"));

        var act = () => Gateway(handler).VerifyAsync("KBABC123", "uuid");

        await act.Should().ThrowAsync<PaymentGatewayException>();
    }

    [Theory]
    [InlineData("wave-cote-divoire", PaymentMethod.Wave)]
    [InlineData("orange-money-cote-divoire", PaymentMethod.OrangeMoney)]
    [InlineData("mtn-cote-divoire", PaymentMethod.Mtn)]
    [InlineData("moov-cote-divoire", PaymentMethod.Moov)]
    [InlineData("visa", PaymentMethod.Card)]
    [InlineData("unknown-operator", PaymentMethod.Other)]
    [InlineData(null, PaymentMethod.Other)]
    public void Operators_are_mapped_to_payment_methods(string? operatorId, PaymentMethod expected) =>
        WinipayGateway.MapMethod(operatorId).Should().Be(expected);
}
