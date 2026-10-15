using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using KekeBeauty.Infrastructure.Billing;
using KekeBeauty.Infrastructure.Persistence;
using KekeBeauty.Modules.Administration.Domain;
using KekeBeauty.Modules.Catalog.Domain;
using KekeBeauty.Modules.Geography.Domain;
using KekeBeauty.SharedKernel;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using NetTopologySuite.Geometries;

namespace KekeBeauty.Infrastructure.Admin;

public sealed record CategoryAdminDto(Guid Id, string Name, string Slug, string Icon, int SortOrder, bool IsActive, int Establishments);

public sealed record CategoryInput(string? Name, string? Icon, int SortOrder, bool IsActive);

public sealed record GeoAdminDto(Guid Id, string Name, bool IsActive, string? Code, int Children);

public sealed record GeoCreateInput(Guid? ParentId, string? Name, string? Iso2, string? PhonePrefix, string? CurrencyCode, double? Latitude, double? Longitude);

public sealed record GeoUpdateInput(string? Name, bool IsActive);

/// <summary>Global parameters edited by the admin team (PRD ADM-05). Channel toggles only switch a channel on when its provider is configured.</summary>
public sealed record AppSettingsDto(
    int GraceDays,
    int SessionIdleMinutes = 60,
    int KycRetentionDays = 365,
    int ReminderFirstHours = 24,
    int ReminderSecondHours = 2,
    bool SmsEnabled = false,
    bool WhatsAppEnabled = false,
    bool PushEnabled = false,
    bool AppointmentPaymentsEnabled = false,
    int PlatformFeePercent = 0);

/// <summary>Global parameters that can be changed without a deployment (PRD ADM-05).</summary>
public sealed class AppSettingsService(KekeBeautyDbContext db, IOptions<BillingOptions> billing, TimeProvider time)
{
    public const string GraceDaysKey = "billing.grace_days";
    public const string SessionIdleKey = "session.idle_minutes";
    public const string KycRetentionKey = "kyc.retention_days";
    public const string ReminderFirstKey = "reminders.first_hours";
    public const string ReminderSecondKey = "reminders.second_hours";
    public const string SmsEnabledKey = "channel.sms_enabled";
    public const string WhatsAppEnabledKey = "channel.whatsapp_enabled";
    public const string PushEnabledKey = "channel.push_enabled";
    public const string AppointmentPaymentsKey = "payments.appointments_enabled";
    public const string PlatformFeeKey = "payments.platform_fee_percent";

    public const int MaxGraceDays = 30;
    public const int DefaultSessionIdleMinutes = 60;
    public const int MinSessionIdleMinutes = 5;
    public const int MaxSessionIdleMinutes = 1440;
    public const int DefaultKycRetentionDays = 365;
    public const int MinKycRetentionDays = 30;
    public const int MaxKycRetentionDays = 3650;
    public const int DefaultReminderFirstHours = 24;
    public const int DefaultReminderSecondHours = 2;
    public const int MaxReminderHours = 168;
    public const int MaxPlatformFeePercent = 30;

    private static readonly string[] AllKeys =
    [
        GraceDaysKey, SessionIdleKey, KycRetentionKey, ReminderFirstKey, ReminderSecondKey,
        SmsEnabledKey, WhatsAppEnabledKey, PushEnabledKey, AppointmentPaymentsKey, PlatformFeeKey,
    ];

    public async Task<int> GraceDaysAsync(CancellationToken ct) => (await LoadAsync(ct)).GraceDays;

    public async Task<TimeSpan> SessionIdleAsync(CancellationToken ct) => TimeSpan.FromMinutes((await LoadAsync(ct)).SessionIdleMinutes);

    public async Task<int> KycRetentionDaysAsync(CancellationToken ct) => (await LoadAsync(ct)).KycRetentionDays;

    public async Task<bool> IsEnabledAsync(string key, CancellationToken ct)
    {
        var stored = await db.AppSettings.AsNoTracking().Where(s => s.Key == key).Select(s => s.Value).FirstOrDefaultAsync(ct);
        return stored == bool.TrueString;
    }

    public Task<AppSettingsDto> GetAsync(CancellationToken ct) => LoadAsync(ct);

    public async Task<Result<AppSettingsDto>> UpdateAsync(AdminContext admin, AppSettingsDto input, CancellationToken ct)
    {
        var problem =
            Check(input.GraceDays, 0, MaxGraceDays, $"Le délai de grâce doit être compris entre 0 et {MaxGraceDays} jours.")
            ?? Check(input.SessionIdleMinutes, MinSessionIdleMinutes, MaxSessionIdleMinutes, $"L'inactivité avant déconnexion doit être comprise entre {MinSessionIdleMinutes} et {MaxSessionIdleMinutes} minutes.")
            ?? Check(input.KycRetentionDays, MinKycRetentionDays, MaxKycRetentionDays, $"La conservation des pièces d'identité doit être comprise entre {MinKycRetentionDays} et {MaxKycRetentionDays} jours.")
            ?? Check(input.ReminderFirstHours, 0, MaxReminderHours, $"Le premier rappel doit être compris entre 0 (désactivé) et {MaxReminderHours} heures avant le rendez-vous.")
            ?? Check(input.ReminderSecondHours, 0, MaxReminderHours, $"Le second rappel doit être compris entre 0 (désactivé) et {MaxReminderHours} heures avant le rendez-vous.")
            ?? Check(input.PlatformFeePercent, 0, MaxPlatformFeePercent, $"La commission de la plateforme doit être comprise entre 0 et {MaxPlatformFeePercent} %.");
        if (problem is not null)
        {
            return Result.Failure<AppSettingsDto>(problem);
        }

        var values = new Dictionary<string, string>
        {
            [GraceDaysKey] = Text(input.GraceDays),
            [SessionIdleKey] = Text(input.SessionIdleMinutes),
            [KycRetentionKey] = Text(input.KycRetentionDays),
            [ReminderFirstKey] = Text(input.ReminderFirstHours),
            [ReminderSecondKey] = Text(input.ReminderSecondHours),
            [SmsEnabledKey] = input.SmsEnabled.ToString(),
            [WhatsAppEnabledKey] = input.WhatsAppEnabled.ToString(),
            [PushEnabledKey] = input.PushEnabled.ToString(),
            [AppointmentPaymentsKey] = input.AppointmentPaymentsEnabled.ToString(),
            [PlatformFeeKey] = Text(input.PlatformFeePercent),
        };

        var rows = await db.AppSettings.Where(s => AllKeys.Contains(s.Key)).ToDictionaryAsync(s => s.Key, ct);
        var now = time.GetUtcNow();
        foreach (var (key, value) in values)
        {
            if (rows.TryGetValue(key, out var row))
            {
                row.Value = value;
                row.UpdatedAt = now;
                row.UpdatedBy = admin.ActorId;
            }
            else
            {
                db.AppSettings.Add(new AppSetting { Key = key, Value = value, UpdatedAt = now, UpdatedBy = admin.ActorId });
            }
        }

        Audit.Add(db, admin, time, "settings.update", "Settings", Guid.Empty, input);
        await db.SaveChangesAsync(ct);
        return Result.Success(input);
    }

    private async Task<AppSettingsDto> LoadAsync(CancellationToken ct)
    {
        var rows = await db.AppSettings.AsNoTracking().Where(s => AllKeys.Contains(s.Key)).ToDictionaryAsync(s => s.Key, s => s.Value, ct);
        return new AppSettingsDto(
            Number(rows, GraceDaysKey, billing.Value.GraceDays, 0, MaxGraceDays),
            Number(rows, SessionIdleKey, DefaultSessionIdleMinutes, MinSessionIdleMinutes, MaxSessionIdleMinutes),
            Number(rows, KycRetentionKey, DefaultKycRetentionDays, MinKycRetentionDays, MaxKycRetentionDays),
            Number(rows, ReminderFirstKey, DefaultReminderFirstHours, 0, MaxReminderHours),
            Number(rows, ReminderSecondKey, DefaultReminderSecondHours, 0, MaxReminderHours),
            Flag(rows, SmsEnabledKey),
            Flag(rows, WhatsAppEnabledKey),
            Flag(rows, PushEnabledKey),
            Flag(rows, AppointmentPaymentsKey),
            Number(rows, PlatformFeeKey, 0, 0, MaxPlatformFeePercent));
    }

    private static int Number(Dictionary<string, string> rows, string key, int fallback, int min, int max) =>
        rows.TryGetValue(key, out var stored)
        && int.TryParse(stored, NumberStyles.None, CultureInfo.InvariantCulture, out var value)
        && value >= min && value <= max
            ? value
            : fallback;

    private static bool Flag(Dictionary<string, string> rows, string key) => rows.TryGetValue(key, out var stored) && stored == bool.TrueString;

    private static string Text(int value) => value.ToString(CultureInfo.InvariantCulture);

    private static Error? Check(int value, int min, int max, string message) => value < min || value > max ? AdminErrors.Invalid(message) : null;
}

/// <summary>Beauty categories and geographic zones maintained from the back-office (PRD ADM-05).</summary>
public sealed partial class AdminSettingsService(KekeBeautyDbContext db, TimeProvider time)
{
    public static readonly string[] GeoLevels = ["countries", "regions", "cities", "communes"];

    [GeneratedRegex("^[a-z0-9-]{1,40}$")]
    private static partial Regex IconPattern();

    [GeneratedRegex("^[A-Z]{2}$")]
    private static partial Regex Iso2Pattern();

    [GeneratedRegex(@"^\+\d{1,4}$")]
    private static partial Regex PrefixPattern();

    [GeneratedRegex("^[A-Z]{3}$")]
    private static partial Regex CurrencyPattern();

    // ───────── Categories ─────────

    public async Task<IReadOnlyList<CategoryAdminDto>> CategoriesAsync(CancellationToken ct)
    {
        var categories = await db.BeautyCategories.AsNoTracking().OrderBy(c => c.SortOrder).ThenBy(c => c.Name).ToListAsync(ct);
        var counts = await db.EstablishmentCategories.AsNoTracking().GroupBy(l => l.CategoryId)
            .Select(g => new { g.Key, Count = g.Count() }).ToDictionaryAsync(x => x.Key, x => x.Count, ct);
        return categories.Select(c => new CategoryAdminDto(c.Id, c.Name, c.Slug, c.Icon, c.SortOrder, c.IsActive, counts.GetValueOrDefault(c.Id))).ToList();
    }

    public async Task<Result<Guid>> CreateCategoryAsync(AdminContext admin, CategoryInput input, CancellationToken ct)
    {
        var problem = ValidateCategory(input, out var name, out var icon);
        if (problem is not null)
        {
            return Result.Failure<Guid>(problem);
        }

        var slug = Slugify(name);
        if (slug.Length == 0 || await db.BeautyCategories.AnyAsync(c => c.Slug == slug || c.Name == name, ct))
        {
            return Result.Failure<Guid>(AdminErrors.Taken);
        }

        var category = new BeautyCategory { Name = name, Slug = slug, Icon = icon, SortOrder = input.SortOrder, IsActive = input.IsActive };
        db.BeautyCategories.Add(category);
        Audit.Add(db, admin, time, "category.create", "Category", category.Id, new { name });
        await db.SaveChangesAsync(ct);
        return Result.Success(category.Id);
    }

    public async Task<Result> UpdateCategoryAsync(AdminContext admin, Guid id, CategoryInput input, CancellationToken ct)
    {
        var problem = ValidateCategory(input, out var name, out var icon);
        if (problem is not null)
        {
            return Result.Failure(problem);
        }

        var category = await db.BeautyCategories.FirstOrDefaultAsync(c => c.Id == id, ct);
        if (category is null)
        {
            return Result.Failure(new Error("category.not_found", "Catégorie introuvable."));
        }

        if (await db.BeautyCategories.AnyAsync(c => c.Id != id && c.Name == name, ct))
        {
            return Result.Failure(AdminErrors.Taken);
        }

        category.Name = name;
        category.Icon = icon;
        category.SortOrder = input.SortOrder;
        category.IsActive = input.IsActive;
        Audit.Add(db, admin, time, "category.update", "Category", id, new { name, input.IsActive });
        await db.SaveChangesAsync(ct);
        return Result.Success();
    }

    private static Error? ValidateCategory(CategoryInput input, out string name, out string icon)
    {
        name = input.Name?.Trim() ?? string.Empty;
        icon = input.Icon?.Trim().ToLowerInvariant() ?? string.Empty;
        if (name.Length is < 2 or > 60)
        {
            return AdminErrors.Invalid("Le nom doit compter de 2 à 60 caractères.");
        }

        if (!IconPattern().IsMatch(icon))
        {
            return AdminErrors.Invalid("L'icône est un identifiant en minuscules (lettres, chiffres, tirets).");
        }

        return input.SortOrder is < 0 or > 1000 ? AdminErrors.Invalid("L'ordre doit être compris entre 0 et 1000.") : null;
    }

    private const string Accented = "àâäáãåçéèêëíìîïñóòôöõúùûüýÿ";
    private const string Plain = "aaaaaaceeeeiiiinooooouuuuyy";

    /// <summary>Lower-case ASCII slug. An explicit table is used because string normalisation is a no-op in invariant-globalization containers.</summary>
    public static string Slugify(string text)
    {
        var builder = new StringBuilder();
        foreach (var c in text.ToLowerInvariant())
        {
            var index = Accented.IndexOf(c, StringComparison.Ordinal);
            builder.Append(c switch
            {
                'œ' => "oe",
                'æ' => "ae",
                _ when index >= 0 => Plain[index].ToString(),
                _ when c is >= 'a' and <= 'z' or >= '0' and <= '9' => c.ToString(),
                _ => "-",
            });
        }

        return Regex.Replace(builder.ToString(), "-{2,}", "-").Trim('-');
    }

    // ───────── Geographic zones ─────────

    public async Task<Result<IReadOnlyList<GeoAdminDto>>> GeoAsync(string level, Guid? parentId, CancellationToken ct)
    {
        if (!GeoLevels.Contains(level))
        {
            return Result.Failure<IReadOnlyList<GeoAdminDto>>(GeoLevelUnknown);
        }

        if (level != "countries" && parentId is null)
        {
            return Result.Failure<IReadOnlyList<GeoAdminDto>>(AdminErrors.Invalid("Indiquez le parent (parentId)."));
        }

        IReadOnlyList<GeoAdminDto> items = level switch
        {
            "countries" => await db.Countries.AsNoTracking().OrderBy(c => c.Name)
                .Select(c => new GeoAdminDto(c.Id, c.Name, c.IsActive, c.Iso2, c.Regions.Count)).ToListAsync(ct),
            "regions" => await db.Regions.AsNoTracking().Where(r => EF.Property<Guid>(r, "CountryId") == parentId).OrderBy(r => r.Name)
                .Select(r => new GeoAdminDto(r.Id, r.Name, r.IsActive, null, r.Cities.Count)).ToListAsync(ct),
            "cities" => await db.Cities.AsNoTracking().Where(c => EF.Property<Guid>(c, "RegionId") == parentId).OrderBy(c => c.Name)
                .Select(c => new GeoAdminDto(c.Id, c.Name, c.IsActive, null, c.Communes.Count)).ToListAsync(ct),
            _ => await db.Communes.AsNoTracking().Where(c => EF.Property<Guid>(c, "CityId") == parentId).OrderBy(c => c.Name)
                .Select(c => new GeoAdminDto(c.Id, c.Name, c.IsActive, null, 0)).ToListAsync(ct),
        };
        return Result.Success(items);
    }

    public async Task<Result<Guid>> CreateGeoAsync(AdminContext admin, string level, GeoCreateInput input, CancellationToken ct)
    {
        if (!GeoLevels.Contains(level))
        {
            return Result.Failure<Guid>(GeoLevelUnknown);
        }

        var name = input.Name?.Trim() ?? string.Empty;
        if (name.Length is < 2 or > 80)
        {
            return Result.Failure<Guid>(AdminErrors.Invalid("Le nom doit compter de 2 à 80 caractères."));
        }

        Guid id;
        switch (level)
        {
            case "countries":
                {
                    var iso2 = input.Iso2?.Trim().ToUpperInvariant() ?? string.Empty;
                    var prefix = input.PhonePrefix?.Trim() ?? string.Empty;
                    var currency = input.CurrencyCode?.Trim().ToUpperInvariant() ?? string.Empty;
                    if (!Iso2Pattern().IsMatch(iso2) || !PrefixPattern().IsMatch(prefix) || !CurrencyPattern().IsMatch(currency))
                    {
                        return Result.Failure<Guid>(AdminErrors.Invalid("Code pays (2 lettres), indicatif (+225) et devise (3 lettres) attendus."));
                    }

                    if (await db.Countries.AnyAsync(c => c.Iso2 == iso2 || c.Name == name, ct))
                    {
                        return Result.Failure<Guid>(AdminErrors.Taken);
                    }

                    var country = new Country { Iso2 = iso2, Name = name, PhonePrefix = prefix, CurrencyCode = currency };
                    db.Countries.Add(country);
                    id = country.Id;
                    break;
                }

            case "regions":
                {
                    var parent = await db.Countries.Include(c => c.Regions).FirstOrDefaultAsync(c => c.Id == input.ParentId, ct);
                    if (parent is null)
                    {
                        return Result.Failure<Guid>(ParentNotFound);
                    }

                    if (parent.Regions.Any(r => string.Equals(r.Name, name, StringComparison.OrdinalIgnoreCase)))
                    {
                        return Result.Failure<Guid>(AdminErrors.Taken);
                    }

                    var region = new Region { Name = name };
                    parent.Regions.Add(region);
                    id = region.Id;
                    break;
                }

            case "cities":
                {
                    var parent = await db.Regions.Include(r => r.Cities).FirstOrDefaultAsync(r => r.Id == input.ParentId, ct);
                    if (parent is null)
                    {
                        return Result.Failure<Guid>(ParentNotFound);
                    }

                    if (parent.Cities.Any(c => string.Equals(c.Name, name, StringComparison.OrdinalIgnoreCase)))
                    {
                        return Result.Failure<Guid>(AdminErrors.Taken);
                    }

                    var city = new City { Name = name };
                    parent.Cities.Add(city);
                    id = city.Id;
                    break;
                }

            default:
                {
                    var parent = await db.Cities.Include(c => c.Communes).FirstOrDefaultAsync(c => c.Id == input.ParentId, ct);
                    if (parent is null)
                    {
                        return Result.Failure<Guid>(ParentNotFound);
                    }

                    if (parent.Communes.Any(c => string.Equals(c.Name, name, StringComparison.OrdinalIgnoreCase)))
                    {
                        return Result.Failure<Guid>(AdminErrors.Taken);
                    }

                    Point? centroid = null;
                    if (input.Latitude is { } lat && input.Longitude is { } lng)
                    {
                        if (lat is < -90 or > 90 || lng is < -180 or > 180)
                        {
                            return Result.Failure<Guid>(AdminErrors.Invalid("Coordonnées hors limites."));
                        }

                        centroid = new Point(lng, lat) { SRID = 4326 };
                    }

                    var commune = new Commune { Name = name, Centroid = centroid };
                    parent.Communes.Add(commune);
                    id = commune.Id;
                    break;
                }
        }

        Audit.Add(db, admin, time, "geo.create", "Geo", id, new { level, name });
        await db.SaveChangesAsync(ct);
        return Result.Success(id);
    }

    public async Task<Result> UpdateGeoAsync(AdminContext admin, string level, Guid id, GeoUpdateInput input, CancellationToken ct)
    {
        if (!GeoLevels.Contains(level))
        {
            return Result.Failure(GeoLevelUnknown);
        }

        var name = input.Name?.Trim() ?? string.Empty;
        if (name.Length is < 2 or > 80)
        {
            return Result.Failure(AdminErrors.Invalid("Le nom doit compter de 2 à 80 caractères."));
        }

        var found = level switch
        {
            "countries" => Apply(await db.Countries.FirstOrDefaultAsync(c => c.Id == id, ct), c => (c.Name, c.IsActive) = (name, input.IsActive)),
            "regions" => Apply(await db.Regions.FirstOrDefaultAsync(r => r.Id == id, ct), r => (r.Name, r.IsActive) = (name, input.IsActive)),
            "cities" => Apply(await db.Cities.FirstOrDefaultAsync(c => c.Id == id, ct), c => (c.Name, c.IsActive) = (name, input.IsActive)),
            _ => Apply(await db.Communes.FirstOrDefaultAsync(c => c.Id == id, ct), c => (c.Name, c.IsActive) = (name, input.IsActive)),
        };
        if (!found)
        {
            return Result.Failure(new Error("geo.not_found", "Zone introuvable."));
        }

        Audit.Add(db, admin, time, "geo.update", "Geo", id, new { level, name, input.IsActive });
        await db.SaveChangesAsync(ct);
        return Result.Success();

        static bool Apply<T>(T? entity, Action<T> change)
            where T : class
        {
            if (entity is null)
            {
                return false;
            }

            change(entity);
            return true;
        }
    }

    private static readonly Error GeoLevelUnknown = new("geo.not_found", "Niveau géographique inconnu (countries, regions, cities, communes).");

    private static readonly Error ParentNotFound = new("geo.not_found", "Zone parente introuvable.");
}
