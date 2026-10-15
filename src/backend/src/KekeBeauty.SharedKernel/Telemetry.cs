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

    public static readonly Counter<long> AppointmentPayments =
        AppMeter.CreateCounter<long>("keke.appointment_payments", description: "Appointment payments settled, tagged with the outcome.");
}
