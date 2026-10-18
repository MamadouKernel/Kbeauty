using KekeBeauty.Infrastructure.Admin;
using KekeBeauty.Infrastructure.Billing;
using KekeBeauty.Infrastructure.Partner;
using KekeBeauty.Infrastructure.Persistence;
using KekeBeauty.Infrastructure.Scheduling;
using KekeBeauty.Infrastructure.Search;
using KekeBeauty.Infrastructure.Storage;
using KekeBeauty.SharedKernel;
using KekeBeauty.Modules.Identity.Application;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace KekeBeauty.Infrastructure;

public static class DependencyInjection
{
    public const string ConnectionStringName = "Default";

    public static IServiceCollection AddInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        var connectionString = configuration.GetConnectionString(ConnectionStringName)
            ?? throw new InvalidOperationException($"ConnectionStrings:{ConnectionStringName} is not configured.");

        services.AddDbContext<KekeBeautyDbContext>(options => options
            .UseNpgsql(connectionString, npgsql => npgsql.UseNetTopologySuite())
            .UseSnakeCaseNamingConvention());

        services.AddScoped<IIdentityDbContext>(provider => provider.GetRequiredService<KekeBeautyDbContext>());

        // Hosted services start in registration order: migrate before Data Protection reads its key ring.
        services.Configure<DatabaseOptions>(configuration.GetSection(DatabaseOptions.SectionName));
        services.AddHostedService<DatabaseInitializer>();

        // Keys live in the database so TOTP secrets stay decryptable across restarts and instances.
        services.AddDataProtection()
            .SetApplicationName("KekeBeauty")
            .PersistKeysToDbContext<KekeBeautyDbContext>();

        services.AddScoped<EstablishmentQueries>();

        services.Configure<StorageOptions>(configuration.GetSection(StorageOptions.SectionName));
        if (string.Equals(configuration[$"{StorageOptions.SectionName}:Provider"], "Memory", StringComparison.OrdinalIgnoreCase))
        {
            services.AddSingleton<IFileStorage, InMemoryFileStorage>();
        }
        else
        {
            services.AddSingleton<IFileStorage, S3FileStorage>();
        }

        services.AddScoped<PartnerEstablishmentService>();
        services.AddScoped<PartnerCatalogService>();
        services.AddScoped<PartnerMediaService>();
        services.AddScoped<AcceptedPaymentsService>();
        services.AddScoped<KycService>();
        services.AddScoped<ReviewService>();

        // A configured SMTP relay replaces the log-only sender (registered first, so the identity module's default does not apply).
        var leTexto = configuration.GetSection(KekeBeauty.Infrastructure.Notifications.LeTextoOptions.SectionName).Get<KekeBeauty.Infrastructure.Notifications.LeTextoOptions>() ?? new();
        services.Configure<KekeBeauty.Infrastructure.Notifications.SmtpOptions>(configuration.GetSection(KekeBeauty.Infrastructure.Notifications.SmtpOptions.SectionName));
        if (string.Equals(configuration["Email:Provider"], "LeTexto", StringComparison.OrdinalIgnoreCase) && leTexto.EmailReady)
        {
            services.AddTransient<IEmailSender, KekeBeauty.Infrastructure.Notifications.LeTextoEmailSender>();
        }
        else if (!string.IsNullOrWhiteSpace(configuration[$"{KekeBeauty.Infrastructure.Notifications.SmtpOptions.SectionName}:Host"]))
        {
            services.AddSingleton<IEmailSender, KekeBeauty.Infrastructure.Notifications.SmtpEmailSender>();
        }

        // LeTexto (SMS and e-mail) is configured but off until LeTexto:Enabled is switched on; the log-only senders stay in use meanwhile.
        services.Configure<KekeBeauty.Infrastructure.Notifications.LeTextoOptions>(configuration.GetSection(KekeBeauty.Infrastructure.Notifications.LeTextoOptions.SectionName));
        services.AddHttpClient<KekeBeauty.Infrastructure.Notifications.LeTextoClient>(client => client.Timeout = TimeSpan.FromSeconds(20));
        if (leTexto.SmsReady)
        {
            services.AddTransient<ISmsSender, KekeBeauty.Infrastructure.Notifications.LeTextoSmsSender>();
        }

        services.TryAddSingleton<ISmsSender, LoggingSmsSender>();

        services.Configure<KekeBeauty.Infrastructure.Notifications.WhatsAppOptions>(configuration.GetSection(KekeBeauty.Infrastructure.Notifications.WhatsAppOptions.SectionName));
        if (string.Equals(configuration[$"{KekeBeauty.Infrastructure.Notifications.WhatsAppOptions.SectionName}:Provider"] ?? "Log", "Log", StringComparison.OrdinalIgnoreCase))
        {
            services.AddSingleton<IWhatsAppSender, KekeBeauty.Infrastructure.Notifications.LoggingWhatsAppSender>();
        }
        else
        {
            services.AddSingleton<IWhatsAppSender, KekeBeauty.Infrastructure.Notifications.UnconfiguredWhatsAppSender>();
        }

        services.TryAddSingleton<IRealtimeNotifier, NullRealtimeNotifier>();
        services.Configure<KekeBeauty.Infrastructure.Notifications.PushOptions>(configuration.GetSection(KekeBeauty.Infrastructure.Notifications.PushOptions.SectionName));
        services.AddSingleton<KekeBeauty.Infrastructure.Notifications.VapidKeyStore>();
        services.AddHttpClient<KekeBeauty.Infrastructure.Notifications.WebPushSender>(client => client.Timeout = TimeSpan.FromSeconds(15));
        services.TryAddTransient<IPushSender>(provider => provider.GetRequiredService<KekeBeauty.Infrastructure.Notifications.WebPushSender>());
        services.AddScoped<NotificationService>();
        services.AddScoped<KekeBeauty.Infrastructure.Notifications.ChannelService>();
        services.AddSingleton<ReminderMaintenance>();
        services.AddHostedService(provider => provider.GetRequiredService<ReminderMaintenance>());
        services.AddScoped<AppointmentWorkflow>();
        services.AddScoped<AvailabilityService>();
        services.AddScoped<BookingService>();
        services.AddScoped<AgendaService>();
        services.AddScoped<CounterService>();
        services.AddScoped<PassService>();
        services.AddScoped<TeamService>();
        services.AddScoped<CommissionService>();
        services.AddScoped<DisputeService>();
        services.AddScoped<KekeBeauty.Infrastructure.Billing.TipPaymentService>();
        services.AddScoped<KekeBeauty.Infrastructure.Billing.CollaboratorPayoutService>();
        services.AddScoped<KekeBeauty.Infrastructure.Reviews.ReviewPhotoService>();
        services.AddScoped<ResourceService>();
        services.AddSingleton<AppointmentMaintenance>();
        services.AddHostedService(provider => provider.GetRequiredService<AppointmentMaintenance>());

        services.Configure<PaymentsOptions>(configuration.GetSection(PaymentsOptions.SectionName));
        services.Configure<BillingOptions>(configuration.GetSection(BillingOptions.SectionName));
        services.AddSingleton<FakePaymentGateway>();
        var paymentProvider = configuration[$"{PaymentsOptions.SectionName}:Provider"];
        if (string.Equals(paymentProvider, "Winipay", StringComparison.OrdinalIgnoreCase))
        {
            services.AddHttpClient<WinipayGateway>(client => client.Timeout = TimeSpan.FromSeconds(30));
            services.AddTransient<IPaymentGateway>(provider => provider.GetRequiredService<WinipayGateway>());
        }
        else if (string.Equals(paymentProvider, "CinetPay", StringComparison.OrdinalIgnoreCase))
        {
            services.AddHttpClient<CinetPayGateway>();
            services.AddTransient<IPaymentGateway>(provider => provider.GetRequiredService<CinetPayGateway>());
        }
        else
        {
            services.AddSingleton<IPaymentGateway>(provider => provider.GetRequiredService<FakePaymentGateway>());
        }

        services.AddScoped<KekeBeauty.Infrastructure.Privacy.PrivacyService>();
        services.AddScoped<AppSettingsService>();
        services.AddScoped<LegalDocumentService>();
        services.AddSingleton<IDatabaseDumper, KekeBeauty.Infrastructure.Backups.PgDumpDatabaseDumper>();
        services.AddScoped<KekeBeauty.Infrastructure.Backups.BackupService>();
        services.AddSingleton<KekeBeauty.Infrastructure.Backups.BackupMaintenance>();
        services.AddHostedService(provider => provider.GetRequiredService<KekeBeauty.Infrastructure.Backups.BackupMaintenance>());
        services.AddScoped<KekeBeauty.Modules.Identity.Application.ISessionPolicy, SessionPolicy>();
        services.AddScoped<AuditQueryService>();
        services.AddScoped<AdminUserService>();
        services.AddScoped<AdminModerationService>();
        services.AddScoped<AdminFinanceService>();
        services.AddScoped<AdminSettingsService>();
        services.AddScoped<NotificationTemplateService>();
        services.AddScoped<EntitlementService>();
        services.AddScoped<PaymentProcessor>();
        services.AddSingleton<FakePayoutGateway>();
        if (string.Equals(paymentProvider, "Winipay", StringComparison.OrdinalIgnoreCase))
        {
            services.AddHttpClient<WinipayPayoutGateway>(client => client.Timeout = TimeSpan.FromSeconds(30));
            services.AddTransient<IPayoutGateway>(provider => provider.GetRequiredService<WinipayPayoutGateway>());
        }
        else
        {
            services.AddSingleton<IPayoutGateway>(provider => provider.GetRequiredService<FakePayoutGateway>());
        }

        services.AddScoped<AppointmentPaymentService>();
        services.AddScoped<PayoutService>();
        services.AddSingleton<PaymentSettlementMaintenance>();
        services.AddHostedService(provider => provider.GetRequiredService<PaymentSettlementMaintenance>());
        services.AddScoped<PromoService>();
        services.AddScoped<KekeBeauty.Infrastructure.Reviews.OpinionService>();
        services.AddScoped<KekeBeauty.Infrastructure.Reviews.FavoriteService>();
        services.AddScoped<BillingService>();
        services.AddSingleton<RetentionMaintenance>();
        services.AddHostedService(provider => provider.GetRequiredService<RetentionMaintenance>());
        services.AddSingleton<BillingMaintenance>();
        services.AddHostedService(provider => provider.GetRequiredService<BillingMaintenance>());

        return services;
    }
}
