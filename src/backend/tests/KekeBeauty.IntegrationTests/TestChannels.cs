using KekeBeauty.Infrastructure.Admin;
using KekeBeauty.Infrastructure.Persistence;
using KekeBeauty.Modules.Administration.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace KekeBeauty.IntegrationTests;

/// <summary>Switches notification channels on or off the way the admins do (the channels are all off by default).</summary>
internal static class TestChannels
{
    public static async Task SetAsync(ApiFixture api, string key, bool enabled)
    {
        await using var scope = api.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<KekeBeautyDbContext>();
        var row = await db.AppSettings.FirstOrDefaultAsync(s => s.Key == key);
        if (row is null)
        {
            db.AppSettings.Add(new AppSetting { Key = key, Value = enabled.ToString(), UpdatedAt = DateTimeOffset.UtcNow });
        }
        else
        {
            row.Value = enabled.ToString();
        }

        await db.SaveChangesAsync();
    }

    public static Task EnableSmsAsync(ApiFixture api) => SetAsync(api, AppSettingsService.SmsEnabledKey, true);
}
