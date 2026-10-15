using System.Diagnostics;
using System.Diagnostics.Metrics;
using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using KekeBeauty.Infrastructure.Billing;
using KekeBeauty.Infrastructure.Scheduling;
using KekeBeauty.SharedKernel;
using Microsoft.Extensions.DependencyInjection;
using OpenTelemetry.Trace;

namespace KekeBeauty.IntegrationTests;

[Collection(ApiCollection.Name)]
public sealed class ObservabilityTests(ApiFixture api)
{
    [Fact]
    public async Task A_booking_produces_a_span_and_a_counter_increment()
    {
        var spans = new List<string>();
        var bookings = 0L;
        using var tracing = new ActivityListener
        {
            ShouldListenTo = source => source.Name == Telemetry.Name,
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllData,
            ActivityStopped = activity => { lock (spans) { spans.Add(activity.OperationName); } },
        };
        ActivitySource.AddActivityListener(tracing);
        using var metrics = new MeterListener();
        metrics.InstrumentPublished = (instrument, listener) =>
        {
            if (instrument.Meter.Name == Telemetry.Name && instrument.Name == "keke.bookings.created")
            {
                listener.EnableMeasurementEvents(instrument);
            }
        };
        metrics.SetMeasurementEventCallback<long>((_, value, _, _) => Interlocked.Add(ref bookings, value));
        metrics.Start();

        using var admin = await TestAccounts.AdminAsync(api);
        var salon = await TestSalons.PublishAsync(api, admin, subscribe: true);
        try
        {
            using var client = await TestAccounts.ClientAsync(api);
            (await salon.Owner.Client.PutAsJsonAsync($"/api/v1/pro/establishments/{salon.Id}/booking-settings", new BookingSettingsDto(true, false, 0, 60, 120, 30)))
                .EnsureSuccessStatusCode();
            var response = await client.Client.PostAsJsonAsync("/api/v1/appointments",
                new BookingRequest(salon.Id, [salon.ServiceHour], null, TestSalons.NextOpenDay(api), "07 00 00 12 34", null));
            response.StatusCode.Should().Be(HttpStatusCode.Created);

            spans.Should().Contain("booking.create");
            Interlocked.Read(ref bookings).Should().Be(1);
        }
        finally
        {
            await TestCleanup.DeleteEstablishmentsAsync(api, salon.Owner.UserId);
        }
    }

    [Fact]
    public void Tracing_is_only_wired_when_enabled()
    {
        api.Services.GetService<TracerProvider>().Should().BeNull();

        using var enabled = api.WithWebHostBuilder(builder => builder.UseSetting("OpenTelemetry:Enabled", "true"));
        enabled.Services.GetService<TracerProvider>().Should().NotBeNull();
    }
}
