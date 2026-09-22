using System.Threading.RateLimiting;
using KekeBeauty.Api.Auth;
using KekeBeauty.Application.Auth;
using KekeBeauty.Application.Directory;
using KekeBeauty.Application.Health;
using KekeBeauty.Application.Onboarding;
using KekeBeauty.Infrastructure;
using KekeBeauty.Infrastructure.Auth;
using KekeBeauty.Infrastructure.Listing;
using KekeBeauty.Infrastructure.Health;
using KekeBeauty.Infrastructure.Onboarding;
using KekeBeauty.Application.Partner;
using KekeBeauty.Application.Rdv;
using KekeBeauty.Application.Billing;
using KekeBeauty.Application.Moderation;
using KekeBeauty.Infrastructure.Partner;
using KekeBeauty.Application.Admin;
using KekeBeauty.Infrastructure.Admin;
using KekeBeauty.Infrastructure.Rdv;
using KekeBeauty.Infrastructure.Billing;
using KekeBeauty.Infrastructure.Moderation;
using KekeBeauty.Application.Staff;
using KekeBeauty.Infrastructure.Staff;
using KekeBeauty.Application.Notifications;
using KekeBeauty.Infrastructure.Notifications;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.

builder.Services.AddControllers();
// Learn more about configuring Swagger/OpenAPI at https://aka.ms/aspnetcore/swashbuckle
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

builder.Services.AddHealthChecks();
builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    options.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(context =>
        RateLimitPartition.GetFixedWindowLimiter(context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
            _ => new FixedWindowRateLimiterOptions { PermitLimit = 120, Window = TimeSpan.FromMinutes(1), QueueLimit = 0 }));
    options.AddPolicy("auth", context => RateLimitPartition.GetFixedWindowLimiter(
        context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
        _ => new FixedWindowRateLimiterOptions { PermitLimit = 10, Window = TimeSpan.FromMinutes(5), QueueLimit = 0 }));
    options.AddPolicy("onboarding", context => RateLimitPartition.GetFixedWindowLimiter(
        context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
        _ => new FixedWindowRateLimiterOptions { PermitLimit = 5, Window = TimeSpan.FromMinutes(10), QueueLimit = 0 }));
    // Geocodage Nominatim : plafonne cote proxy (en plus du cache serveur) pour rester dans une
    // utilisation raisonnable de l'API publique OSM, tout en restant assez genereux pour une
    // recherche d'adresse "au fil de la frappe" cote client (debounce applique en JS).
    options.AddPolicy("geo", context => RateLimitPartition.GetFixedWindowLimiter(
        context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
        _ => new FixedWindowRateLimiterOptions { PermitLimit = 30, Window = TimeSpan.FromMinutes(1), QueueLimit = 0 }));
});

builder.Services.AddSingleton<IDbConnectionFactory, DbConnectionFactory>();
builder.Services.AddScoped<IHealthDataCheck, HealthDataCheck>();
builder.Services.AddHostedService<KekeBeauty.Api.BackgroundJobs.DatabaseMigrationHostedService>();

builder.Services.AddMemoryCache();
builder.Services.AddHttpClient<KekeBeauty.Application.Geo.IGeocodingService, KekeBeauty.Infrastructure.Geo.NominatimGeocodingService>(client =>
{
    // Politique d'usage Nominatim : un User-Agent identifiant l'application est obligatoire.
    client.BaseAddress = new Uri(builder.Configuration["Geo:NominatimBaseUrl"] ?? "https://nominatim.openstreetmap.org/");
    client.DefaultRequestHeaders.UserAgent.ParseAdd("KekeBeauty/1.0 (contact: support@kekebeauty.ci)");
});

builder.Services.AddHttpClient<IOtpSender, ZavuWhatsAppOtpSender>(client =>
{
    var baseUrl = builder.Configuration["Zavu:BaseUrl"] ?? "https://api.zavu.dev";
    client.BaseAddress = new Uri(baseUrl);
});
builder.Services.AddScoped<IOtpChallengeRepository, OtpChallengeRepository>();
builder.Services.AddScoped<IUtilisateurRepository, UtilisateurRepository>();
builder.Services.AddScoped<RequestOtpUseCase>();
builder.Services.AddScoped<VerifyOtpUseCase>();

builder.Services.AddHttpClient<IPartnerNotifier, ZavuWhatsAppPartnerNotifier>(client =>
{
    var baseUrl = builder.Configuration["Zavu:BaseUrl"] ?? "https://api.zavu.dev";
    client.BaseAddress = new Uri(baseUrl);
});
builder.Services.AddScoped<IEtablissementRepository, EtablissementRepository>();
builder.Services.AddSingleton<IFileStorage, LocalFileStorage>();
builder.Services.AddScoped<SubmitPartnerApplicationUseCase>();
builder.Services.AddScoped<ListPendingApplicationsUseCase>();
builder.Services.AddScoped<ValidateApplicationUseCase>();
builder.Services.AddScoped<RejectApplicationUseCase>();
builder.Services.AddScoped<AdminApiKeyFilter>();
builder.Services.AddSingleton<AdminSessionTokenService>();
builder.Services.AddScoped<IAdminManagementRepository, AdminManagementRepository>();

builder.Services.AddScoped<IDirectoryRepository, DirectoryRepository>();
builder.Services.AddScoped<IFavoriRepository, FavoriRepository>();
builder.Services.AddScoped<ToggleFavoriUseCase>();
builder.Services.AddScoped<ICategorieRepository, CategorieRepository>();
builder.Services.AddScoped<IPrestationRepository, PrestationRepository>();
builder.Services.AddScoped<SearchEtablissementsUseCase>();
builder.Services.AddScoped<GetEtablissementDetailUseCase>();
builder.Services.AddScoped<AssignCategoryUseCase>();
builder.Services.AddScoped<AddPrestationUseCase>();

builder.Services.AddScoped<IPartnerPrestationRepository, PartnerPrestationRepository>();
builder.Services.AddScoped<ManagePrestationsUseCase>();
builder.Services.AddScoped<ManageCategoriesUseCase>();
builder.Services.AddScoped<PartnerOwnershipFilter>();
builder.Services.AddSingleton<PartnerSessionTokenService>();
builder.Services.AddSingleton<UserSessionTokenService>();
builder.Services.AddSingleton<DeviceTrustTokenService>();
builder.Services.AddSingleton<StepUpTicketService>();
builder.Services.AddScoped<IStepUpChallengeRepository, StepUpChallengeRepository>();
builder.Services.AddScoped<IEmailSender, SmtpEmailSender>();
builder.Services.AddScoped<RequestStepUpUseCase>();
builder.Services.AddScoped<VerifyStepUpUseCase>();
builder.Services.AddSingleton<RdvQrTokenService>();
builder.Services.AddScoped<IPartnerStatsRepository, PartnerStatsRepository>();
builder.Services.AddScoped<ICollaborateurRepository, CollaborateurRepository>();
builder.Services.AddScoped<ManageEquipeUseCase>();
builder.Services.AddScoped<AssignerCollaborateurUseCase>();
builder.Services.AddScoped<LinkCollaborateurCompteUseCase>();
builder.Services.AddScoped<IAdminStatsRepository, AdminStatsRepository>();
builder.Services.AddScoped<GererLitigesUseCase>();
builder.Services.AddScoped<GererRemboursementsUseCase>();

// Feature 018 (Parcours 5) : portail collaboratrice.
builder.Services.AddScoped<IStaffRepository, StaffRepository>();
builder.Services.AddScoped<StaffPortalUseCase>();
builder.Services.AddScoped<StaffAuthFilter>();

// Feature 018 (Parcours 3 et 6) : pourboire et litiges.
builder.Services.AddScoped<IPourboireRepository, PourboireRepository>();
builder.Services.AddScoped<LaisserPourboireUseCase>();
builder.Services.AddScoped<ILitigeRepository, LitigeRepository>();
builder.Services.AddScoped<DeclarerLitigeUseCase>();

builder.Services.AddScoped<IRdvRepository, RdvRepository>();
builder.Services.AddScoped<INotificationRepository, NotificationRepository>();
builder.Services.AddScoped<NotificationCenterUseCase>();
builder.Services.AddScoped<IPushSubscriptionRepository, PushSubscriptionRepository>();
builder.Services.AddScoped<IPushNotificationSender, WebPushNotificationSender>();
builder.Services.AddScoped<ClientNotificationService>();
builder.Services.AddScoped<MarquerRdvsTerminesUseCase>();
builder.Services.AddScoped<EnvoyerRappelsRdvUseCase>();
builder.Services.AddHostedService<KekeBeauty.Api.BackgroundJobs.RdvCompletionBackgroundService>();
builder.Services.AddHttpClient<IRdvNotifier, ZavuWhatsAppRdvNotifier>(client =>
{
    var baseUrl = builder.Configuration["Zavu:BaseUrl"] ?? "https://api.zavu.dev";
    client.BaseAddress = new Uri(baseUrl);
});
builder.Services.AddScoped<RequestRdvUseCase>();
builder.Services.AddScoped<DecideRdvUseCase>();
builder.Services.AddScoped<IRdvPaiementRepository, RdvPaiementRepository>();
builder.Services.AddScoped<InitierPaiementRdvUseCase>();
builder.Services.AddScoped<VerifyRdvPaiementUseCase>();
builder.Services.AddScoped<IAvisRepository, AvisRepository>();
builder.Services.AddScoped<LaisserAvisUseCase>();
builder.Services.AddScoped<RelancerPaiementRdvUseCase>();
builder.Services.AddScoped<AnnulerRdvUseCase>();

builder.Services.AddScoped<IAbonnementRepository, AbonnementRepository>();
builder.Services.AddScoped<IPlanAccessRepository, PlanAccessRepository>();
builder.Services.AddScoped<PlanAccessService>();
builder.Services.AddHttpClient<IWaveMoneyGateway, WaveMoneyGateway>(client =>
{
    client.BaseAddress = new Uri(builder.Configuration["Payments:Wave:ApiBaseUrl"] ?? "https://api.wave.com/");
});
builder.Services.AddHostedService<KekeBeauty.Api.BackgroundJobs.WavePayoutBackgroundService>();
builder.Services.AddHttpClient<IPaymentGateway, WinPayerGateway>(client =>
{
    var baseUrl = builder.Configuration["Billing:WiniPayer:BaseUrl"] ?? "https://api-v2.winipayer.com";
    client.BaseAddress = new Uri(baseUrl);
});
builder.Services.AddHttpClient<IAbonnementNotifier, ZavuWhatsAppAbonnementNotifier>(client =>
{
    var baseUrl = builder.Configuration["Zavu:BaseUrl"] ?? "https://api.zavu.dev";
    client.BaseAddress = new Uri(baseUrl);
});
builder.Services.AddScoped<SubscribeUseCase>();
builder.Services.AddScoped<AdminListAbonnementsUseCase>();
builder.Services.AddScoped<RelanceUseCase>();
builder.Services.AddScoped<VerifyAbonnementPaiementUseCase>();

builder.Services.AddScoped<IModerationRepository, ModerationRepository>();
builder.Services.AddScoped<SuspendUtilisateurUseCase>();
builder.Services.AddScoped<SuspendEtablissementUseCase>();

var app = builder.Build();

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseHttpsRedirection();
app.Use(async (context, next) =>
{
    context.Response.OnStarting(() =>
    {
        context.Response.Headers["X-Content-Type-Options"] = "nosniff";
        context.Response.Headers["X-Frame-Options"] = "DENY";
        context.Response.Headers["Referrer-Policy"] = "no-referrer";
        context.Response.Headers["Permissions-Policy"] = "camera=(), geolocation=(), microphone=()";
        return Task.CompletedTask;
    });
    await next();
});
app.UseRateLimiter();
app.UseMiddleware<ClientSessionMiddleware>();

app.UseAuthorization();

app.MapControllers();

// FR-002 : point de verification de sante applicative (US1) - ne depend pas de la base de donnees.
app.MapHealthChecks("/health");

// FR-003/FR-004 : verification explicite de lecture/ecriture reelle sur la base de donnees (US2).
// Ne bloque jamais le demarrage du processus (Edge Case - voir research.md, Decision 5) : le
// resultat reflete simplement l'etat courant a chaque appel.
app.MapGet("/health/db", async (IHealthDataCheck healthDataCheck, CancellationToken cancellationToken) =>
{
    var result = await healthDataCheck.CheckAsync(cancellationToken);
    return result.IsHealthy
        ? Results.Ok(new { status = "Healthy", canRead = result.CanRead, canWrite = result.CanWrite })
        : Results.Json(
            new { status = "Unhealthy", canRead = result.CanRead, canWrite = result.CanWrite, error = result.ErrorMessage },
            statusCode: StatusCodes.Status503ServiceUnavailable);
});

app.Run();

