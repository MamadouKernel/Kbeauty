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
using KekeBeauty.Infrastructure.Partner;
using KekeBeauty.Infrastructure.Rdv;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.

builder.Services.AddControllers();
// Learn more about configuring Swagger/OpenAPI at https://aka.ms/aspnetcore/swashbuckle
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

builder.Services.AddHealthChecks();

builder.Services.AddSingleton<IDbConnectionFactory, DbConnectionFactory>();
builder.Services.AddScoped<IHealthDataCheck, HealthDataCheck>();

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

builder.Services.AddScoped<IDirectoryRepository, DirectoryRepository>();
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

builder.Services.AddScoped<IRdvRepository, RdvRepository>();
builder.Services.AddHttpClient<IRdvNotifier, ZavuWhatsAppRdvNotifier>(client =>
{
    var baseUrl = builder.Configuration["Zavu:BaseUrl"] ?? "https://api.zavu.dev";
    client.BaseAddress = new Uri(baseUrl);
});
builder.Services.AddScoped<RequestRdvUseCase>();
builder.Services.AddScoped<DecideRdvUseCase>();

var app = builder.Build();

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseHttpsRedirection();

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
