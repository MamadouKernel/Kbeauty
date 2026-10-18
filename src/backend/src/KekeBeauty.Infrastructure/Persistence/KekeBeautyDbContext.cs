using KekeBeauty.Modules.Billing.Domain;
using KekeBeauty.Modules.Billing.Infrastructure;
using KekeBeauty.Modules.Catalog.Domain;
using KekeBeauty.Modules.Catalog.Infrastructure;
using KekeBeauty.Modules.Administration.Domain;
using KekeBeauty.Modules.Administration.Infrastructure;
using KekeBeauty.Modules.Geography.Domain;
using KekeBeauty.Modules.Geography.Infrastructure;
using KekeBeauty.Modules.Identity.Application;
using KekeBeauty.Modules.Notifications.Domain;
using KekeBeauty.Modules.Notifications.Infrastructure;
using KekeBeauty.Modules.Onboarding.Domain;
using KekeBeauty.Modules.Onboarding.Infrastructure;
using KekeBeauty.Modules.Scheduling.Domain;
using KekeBeauty.Modules.Scheduling.Infrastructure;
using KekeBeauty.Modules.Identity.Domain;
using KekeBeauty.Modules.Identity.Infrastructure;
using Microsoft.AspNetCore.DataProtection.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace KekeBeauty.Infrastructure.Persistence;

/// <summary>
/// Single DbContext, one PostgreSQL schema per module (PRD §8.1). Each module owns its
/// entity configurations; this context only composes them.
/// </summary>
public sealed class KekeBeautyDbContext(DbContextOptions<KekeBeautyDbContext> options)
    : DbContext(options), IIdentityDbContext, IDataProtectionKeyContext
{
    private static readonly string[] RequiredExtensions = ["postgis", "btree_gist", "citext", "unaccent", "pg_trgm"];

    public DbSet<Country> Countries => Set<Country>();
    public DbSet<Region> Regions => Set<Region>();
    public DbSet<City> Cities => Set<City>();
    public DbSet<Commune> Communes => Set<Commune>();
    public DbSet<BeautyCategory> BeautyCategories => Set<BeautyCategory>();
    public DbSet<Establishment> Establishments => Set<Establishment>();
    public DbSet<EstablishmentCategory> EstablishmentCategories => Set<EstablishmentCategory>();
    public DbSet<Review> Reviews => Set<Review>();
    public DbSet<Favorite> Favorites => Set<Favorite>();
    public DbSet<Media> MediaItems => Set<Media>();
    public DbSet<ServiceGroup> ServiceGroups => Set<ServiceGroup>();
    public DbSet<Service> Services => Set<Service>();
    public DbSet<EstablishmentStatsDaily> StatsDaily => Set<EstablishmentStatsDaily>();
    public DbSet<OpeningHour> OpeningHours => Set<OpeningHour>();
    public DbSet<Resource> Resources => Set<Resource>();
    public DbSet<ResourceServiceLink> ResourceServiceLinks => Set<ResourceServiceLink>();
    public DbSet<TimeOff> TimeOffs => Set<TimeOff>();
    public DbSet<ClientTechnicalNote> ClientTechnicalNotes => Set<ClientTechnicalNote>();
    public DbSet<Dispute> Disputes => Set<Dispute>();
    public DbSet<Appointment> Appointments => Set<Appointment>();
    public DbSet<AppointmentLine> AppointmentLines => Set<AppointmentLine>();
    public DbSet<AppointmentStatusHistory> AppointmentHistory => Set<AppointmentStatusHistory>();
    public DbSet<Notification> Notifications => Set<Notification>();
    public DbSet<PushSubscription> PushSubscriptions => Set<PushSubscription>();
    public DbSet<NotificationPreference> NotificationPreferences => Set<NotificationPreference>();
    public DbSet<Plan> Plans => Set<Plan>();
    public DbSet<PromoCode> PromoCodes => Set<PromoCode>();
    public DbSet<PlanPriceHistory> PlanPriceHistory => Set<PlanPriceHistory>();
    public DbSet<Subscription> Subscriptions => Set<Subscription>();
    public DbSet<Invoice> Invoices => Set<Invoice>();
    public DbSet<Payment> Payments => Set<Payment>();
    public DbSet<PaymentWebhookEvent> PaymentWebhookEvents => Set<PaymentWebhookEvent>();
    public DbSet<AppointmentPayment> AppointmentPayments => Set<AppointmentPayment>();
    public DbSet<Payout> Payouts => Set<Payout>();
    public DbSet<InvoiceCounter> InvoiceCounters => Set<InvoiceCounter>();
    public DbSet<KycIdentity> KycIdentities => Set<KycIdentity>();
    public DbSet<EstablishmentReview> EstablishmentReviews => Set<EstablishmentReview>();
    public DbSet<AuditLog> AuditLogs => Set<AuditLog>();
    public DbSet<AppSetting> AppSettings => Set<AppSetting>();
    public DbSet<NotificationTemplate> NotificationTemplates => Set<NotificationTemplate>();
    public DbSet<LegalDocument> LegalDocuments => Set<LegalDocument>();
    public DbSet<DatabaseBackup> DatabaseBackups => Set<DatabaseBackup>();
    public DbSet<LegalDocumentVersion> LegalDocumentVersions => Set<LegalDocumentVersion>();
    public DbSet<User> Users => Set<User>();
    public DbSet<RefreshToken> RefreshTokens => Set<RefreshToken>();
    public DbSet<EmailToken> EmailTokens => Set<EmailToken>();
    public DbSet<LoginChallenge> LoginChallenges => Set<LoginChallenge>();
    public DbSet<RecoveryCode> RecoveryCodes => Set<RecoveryCode>();
    public DbSet<PhoneCode> PhoneCodes => Set<PhoneCode>();
    public DbSet<DataProtectionKey> DataProtectionKeys => Set<DataProtectionKey>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        foreach (var extension in RequiredExtensions)
        {
            modelBuilder.HasPostgresExtension(extension);
        }

        modelBuilder.ApplyConfigurationsFromAssembly(typeof(CountryConfiguration).Assembly);
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(BeautyCategoryConfiguration).Assembly);
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(UserConfiguration).Assembly);
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(OpeningHourConfiguration).Assembly);
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(KycIdentityConfiguration).Assembly);
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(NotificationConfiguration).Assembly);
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(PlanConfiguration).Assembly);
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(AuditLogConfiguration).Assembly);

        // PostGIS proximity search (ST_DWithin / ST_Distance) and trigram name search (PRD §8.3).
        modelBuilder.Entity<Establishment>().HasIndex(x => x.Location).HasMethod("gist");
        modelBuilder.Entity<Establishment>().HasIndex(x => x.Name).HasMethod("gin").HasOperators("gin_trgm_ops");

        modelBuilder
            .HasDbFunction(typeof(PostgresFunctions).GetMethod(nameof(PostgresFunctions.Unaccent), [typeof(string)])!)
            .HasName("keke_unaccent")
            .HasSchema("public");
        modelBuilder.Entity<DataProtectionKey>().ToTable("data_protection_keys", "identity");
    }
}
