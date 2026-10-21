using System.Diagnostics;
using System.Diagnostics.Metrics;

namespace KekeBeauty.SharedKernel;

/// <summary>
/// The application's own traces and metrics (OpenTelemetry, PRD §10.4). Nothing is exported unless the API has an OTLP endpoint
/// configured; creating spans and counting costs next to nothing while no listener is attached.
/// </summary>
public static class Telemetry
{
    public const string Name = "KekeBeauty";

    public static readonly ActivitySource Source = new(Name);

    private static readonly Meter AppMeter = new(Name);

    public static readonly Counter<long> BookingsCreated =
        AppMeter.CreateCounter<long>("keke.bookings.created", description: "Appointments requested by clients.");

    /// <summary>Subscription, appointment and tip payments that reached a final state. Tags: provider, outcome (succeeded, failed, amount_mismatch).</summary>
    public static readonly Counter<long> PaymentsSettled =
        AppMeter.CreateCounter<long>("keke.payments.settled", description: "Payments that reached a final state, tagged with provider and outcome.");

    /// <summary>Notifications received from a payment provider. Tags: provider, result (rejected_signature, processed, duplicate, deferred, ignored).</summary>
    public static readonly Counter<long> PaymentNotifications =
        AppMeter.CreateCounter<long>("keke.payments.notifications", description: "Payment provider notifications, tagged with provider and result.");

    /// <summary>Calls to a payment provider that failed (unreachable, unreadable or refused answer). Tags: provider, operation.</summary>
    public static readonly Counter<long> ProviderErrors =
        AppMeter.CreateCounter<long>("keke.payments.provider_errors", description: "Failed calls to a payment provider.");

    /// <summary>Sign-in attempts that did not succeed. Tag: reason (bad_credentials, account_locked).</summary>
    public static readonly Counter<long> FailedLogins =
        AppMeter.CreateCounter<long>("keke.auth.failed_logins", description: "Failed sign-in attempts, tagged with the reason.");

    /// <summary>Wrong second-factor codes (authenticator or recovery).</summary>
    public static readonly Counter<long> FailedSecondFactors =
        AppMeter.CreateCounter<long>("keke.auth.failed_second_factor", description: "Wrong second-factor codes.");

    /// <summary>Times an account's second factor was locked after repeated wrong codes: a guessing attack in progress.</summary>
    public static readonly Counter<long> SecondFactorLockouts =
        AppMeter.CreateCounter<long>("keke.auth.second_factor_lockouts", description: "Second factors locked after repeated wrong codes.");

    /// <summary>Failures of the shared rate-limit store (Redis): the limits then run per instance until it is back.</summary>
    public static readonly Counter<long> RateLimitStoreErrors =
        AppMeter.CreateCounter<long>("keke.ratelimit.store_errors", description: "Shared rate-limit store failures (limits fall back to per instance).");

    public static readonly Counter<long> AppointmentPayments =
        AppMeter.CreateCounter<long>("keke.appointment_payments", description: "Appointment payments settled, tagged with the outcome.");
}
