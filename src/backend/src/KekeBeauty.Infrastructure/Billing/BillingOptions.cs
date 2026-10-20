namespace KekeBeauty.Infrastructure.Billing;

public sealed class CinetPayOptions
{
    public string ApiKey { get; init; } = string.Empty;
    public string SiteId { get; init; } = string.Empty;

    /// <summary>Secret used to verify the HMAC token of notifications. Never logged, never sent to the browser.</summary>
    public string SecretKey { get; init; } = string.Empty;

    public string BaseUrl { get; init; } = "https://api-checkout.cinetpay.com/v2";
}

/// <summary>WiniPayer checkout (docs.winipayer.com, API 2.0): the merchant's application key and token come from its Manager.</summary>
public sealed class WinipayOptions
{
    /// <summary>Header <c>X-Merchant-Apply</c>.</summary>
    public string ApplyKey { get; init; } = string.Empty;

    /// <summary>Header <c>X-Merchant-Token</c>. Secret: also keys the signature of our own callback URLs.</summary>
    public string Token { get; init; } = string.Empty;

    /// <summary><c>test</c> or <c>prod</c>: a TEST merchant account only works with <c>test</c>, a PROD one with <c>prod</c>.</summary>
    public string Env { get; init; } = "test";

    public string BaseUrl { get; init; } = "https://api-v2.winipayer.com";
}

public sealed class PaymentsOptions
{
    public const string SectionName = "Payments";
    public const string FakeProvider = "fake";
    public const string CinetPayProvider = "cinetpay";
    public const string WinipayProvider = "winipay";

    /// <summary><c>Fake</c> (development and tests only), <c>Winipay</c> or <c>CinetPay</c>.</summary>
    public string Provider { get; init; } = "Fake";

    /// <summary>Public address of the web application: where the customer is sent back after paying.</summary>
    public string WebBaseUrl { get; init; } = "http://localhost:4200";

    /// <summary>Public address of the API: where the provider posts its notifications.</summary>
    public string ApiBaseUrl { get; init; } = "http://localhost:8080";

    /// <summary>Shared secret of the simulated gateway: random for each process, never a fixed published value.</summary>
    public string FakeSecret { get; init; } = Convert.ToHexStringLower(System.Security.Cryptography.RandomNumberGenerator.GetBytes(16));
    public CinetPayOptions CinetPay { get; init; } = new();
    public WinipayOptions Winipay { get; init; } = new();

    public bool IsFake => string.Equals(Provider, "Fake", StringComparison.OrdinalIgnoreCase);
}

public sealed class BillingOptions
{
    public const string SectionName = "Billing";

    /// <summary>Days after the due date before an unpaid subscription loses its premium features (PRD BIL-05).</summary>
    public int GraceDays { get; init; } = 7;
}
