using KekeBeauty.SharedKernel;
using Npgsql;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;

namespace KekeBeauty.Api.Observability;

/// <summary>
/// OpenTelemetry traces and metrics (PRD §10.4). Off unless <c>OpenTelemetry:Enabled=true</c> or an OTLP endpoint is configured
/// (<c>OpenTelemetry:OtlpEndpoint</c>, e.g. <c>http://jaeger:4317</c>); the console exporter is available for local debugging.
/// </summary>
public static class ObservabilityExtensions
{
    public static IServiceCollection AddObservability(this IServiceCollection services, IConfiguration configuration)
    {
        var endpoint = configuration["OpenTelemetry:OtlpEndpoint"];
        if (!configuration.GetValue("OpenTelemetry:Enabled", !string.IsNullOrWhiteSpace(endpoint)))
        {
            return services;
        }

        var serviceName = configuration["OpenTelemetry:ServiceName"] ?? "keke-beauty-api";
        var instance = configuration["OpenTelemetry:ServiceInstance"] ?? Environment.MachineName;
        var protocol = string.Equals(configuration["OpenTelemetry:Protocol"], "http", StringComparison.OrdinalIgnoreCase)
            ? OpenTelemetry.Exporter.OtlpExportProtocol.HttpProtobuf
            : OpenTelemetry.Exporter.OtlpExportProtocol.Grpc;

        services.AddOpenTelemetry()
            .ConfigureResource(resource => resource.AddService(serviceName, serviceInstanceId: instance))
            .WithTracing(tracing =>
            {
                tracing
                    .AddAspNetCoreInstrumentation(options =>
                        // Probes and the long-lived SignalR connections would drown the useful traces.
                        options.Filter = context => !context.Request.Path.StartsWithSegments("/health") && !context.Request.Path.StartsWithSegments("/hubs"))
                    .AddHttpClientInstrumentation()
                    .AddNpgsql()
                    .AddSource(Telemetry.Name);
                if (!string.IsNullOrWhiteSpace(endpoint))
                {
                    tracing.AddOtlpExporter(options =>
                    {
                        options.Endpoint = new Uri(endpoint);
                        options.Protocol = protocol;
                    });
                }
            })
            .WithMetrics(metrics =>
            {
                metrics
                    .AddAspNetCoreInstrumentation()
                    .AddHttpClientInstrumentation()
                    .AddRuntimeInstrumentation()
                    .AddNpgsqlInstrumentation()
                    .AddMeter(Telemetry.Name);
                if (!string.IsNullOrWhiteSpace(endpoint))
                {
                    metrics.AddOtlpExporter(options =>
                    {
                        options.Endpoint = new Uri(endpoint);
                        options.Protocol = protocol;
                    });
                }
            });

        return services;
    }
}
