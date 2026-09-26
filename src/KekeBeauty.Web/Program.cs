using Microsoft.AspNetCore.DataProtection;
using KekeBeauty.Web.Components;
using KekeBeauty.Web.Services;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
var keysPath = builder.Configuration["DataProtection:KeysPath"] ?? Path.Combine(builder.Environment.ContentRootPath, "data-protection-keys");
builder.Services.AddDataProtection().PersistKeysToFileSystem(new DirectoryInfo(keysPath)).SetApplicationName("KekeBeauty.Web");
builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents(options =>
    {
        // Les navigateurs mobiles suspendent parfois l'onglet pendant la fenêtre Google.
        // Garder le circuit permet de reprendre la connexion au retour sans perdre le parcours.
        options.DisconnectedCircuitRetentionPeriod = TimeSpan.FromMinutes(10);
        options.DisconnectedCircuitMaxRetained = 500;
    });

var apiBaseUrl = builder.Configuration["Api:BaseUrl"] ?? "http://localhost:5080";
builder.Services.AddTransient<ClientAuthHandler>();
builder.Services.AddTransient<StaffAuthHandler>();
builder.Services.AddHttpClient<DirectoryApiClient>(client => client.BaseAddress = new Uri(apiBaseUrl)).AddHttpMessageHandler<ClientAuthHandler>();
builder.Services.AddHttpClient<AuthApiClient>(client => client.BaseAddress = new Uri(apiBaseUrl));
builder.Services.AddHttpClient<RdvApiClient>(client => client.BaseAddress = new Uri(apiBaseUrl)).AddHttpMessageHandler<ClientAuthHandler>();
builder.Services.AddTransient<PartnerAuthHandler>();
builder.Services.AddHttpClient<PartnerApiClient>(client => client.BaseAddress = new Uri(apiBaseUrl)).AddHttpMessageHandler<PartnerAuthHandler>();
builder.Services.AddHttpClient<PartnerRdvApiClient>(client => client.BaseAddress = new Uri(apiBaseUrl)).AddHttpMessageHandler<PartnerAuthHandler>();
builder.Services.AddHttpClient<AdminApiClient>(client => client.BaseAddress = new Uri(apiBaseUrl));
builder.Services.AddHttpClient<StaffApiClient>(client => client.BaseAddress = new Uri(apiBaseUrl)).AddHttpMessageHandler<StaffAuthHandler>();
builder.Services.AddHttpClient<NotificationApiClient>(client => client.BaseAddress = new Uri(apiBaseUrl)).AddHttpMessageHandler<ClientAuthHandler>();
builder.Services.AddHttpClient<PushApiClient>(client => client.BaseAddress = new Uri(apiBaseUrl)).AddHttpMessageHandler<ClientAuthHandler>();
builder.Services.AddHttpClient<ProfileApiClient>(client => client.BaseAddress = new Uri(apiBaseUrl)).AddHttpMessageHandler<ClientAuthHandler>();
builder.Services.AddHttpClient<GeoApiClient>(client => client.BaseAddress = new Uri(apiBaseUrl));
builder.Services.AddHttpClient("ApiMedia", client => client.BaseAddress = new Uri(apiBaseUrl));
builder.Services.AddScoped<ClientSessionService>();
builder.Services.AddScoped<ClientBookingFlowService>();
builder.Services.AddScoped<PartnerSessionService>();
builder.Services.AddScoped<AdminSessionService>();
builder.Services.AddScoped<StaffSessionService>();
builder.Services.AddScoped<ToastService>();

var app = builder.Build();

// Configure the HTTP request pipeline.
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
    // The default HSTS value is 30 days. You may want to change this for production scenarios, see https://aka.ms/aspnetcore-hsts.
    app.UseHsts();
}

app.UseHttpsRedirection();
app.Use(async (context, next) =>
{
    context.Response.OnStarting(() =>
    {
        var h = context.Response.Headers;
        h["X-Content-Type-Options"] = "nosniff";
        h["X-Frame-Options"] = "DENY";
        h["Referrer-Policy"] = "strict-origin-when-cross-origin";
        h["Permissions-Policy"] = "camera=(self), geolocation=(self), microphone=()";
        h["Content-Security-Policy"] = "default-src 'self'; base-uri 'self'; form-action 'self'; frame-ancestors 'none'; object-src 'self' data:; script-src 'self' 'unsafe-inline' https://accounts.google.com; style-src 'self' 'unsafe-inline' https://accounts.google.com; font-src 'self' data:; img-src 'self' data: blob: https://*.tile.openstreetmap.org; connect-src 'self' ws: wss: https://accounts.google.com; frame-src https://accounts.google.com";
        return Task.CompletedTask;
    });
    await next();
});

app.UseStaticFiles();
app.UseAntiforgery();

app.MapGet("/media/{id:guid}", async (Guid id, IHttpClientFactory factory, CancellationToken ct) =>
{
    var response = await factory.CreateClient("ApiMedia").GetAsync($"media/{id}", ct);
    if (!response.IsSuccessStatusCode) return Results.NotFound();
    var bytes = await response.Content.ReadAsByteArrayAsync(ct);
    return Results.Bytes(bytes, response.Content.Headers.ContentType?.MediaType ?? "image/jpeg");
});

app.MapGet("/media/devanture/{id:guid}", async (Guid id, IHttpClientFactory factory, CancellationToken ct) =>
{
    var response = await factory.CreateClient("ApiMedia").GetAsync($"media/devanture/{id}", ct);
    if (!response.IsSuccessStatusCode) return Results.NotFound();
    var bytes = await response.Content.ReadAsByteArrayAsync(ct);
    return Results.Bytes(bytes, response.Content.Headers.ContentType?.MediaType ?? "image/jpeg");
});
app.MapGet("/media/avis/{id:guid}/{type}", async (Guid id, string type, IHttpClientFactory factory, CancellationToken ct) =>
{
    var response = await factory.CreateClient("ApiMedia").GetAsync($"media/avis/{id}/{type}", ct);
    if (!response.IsSuccessStatusCode) return Results.NotFound();
    var bytes = await response.Content.ReadAsByteArrayAsync(ct);
    return Results.Bytes(bytes, response.Content.Headers.ContentType?.MediaType ?? "image/jpeg");
});
app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

app.Run();


