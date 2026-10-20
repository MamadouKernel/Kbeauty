using FluentAssertions;
using KekeBeauty.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace KekeBeauty.IntegrationTests;

[Collection(ApiCollection.Name)]
public sealed class DataProtectionAtRestTests(ApiFixture api)
{
    [Fact]
    public async Task The_keys_that_protect_the_second_factors_are_never_stored_in_clear_in_the_database()
    {
        // Enrolling a second factor makes the framework create (or read) a key; partners must enrol one.
        using var partner = await TestAccounts.PartnerAsync(api);

        await using var scope = api.Services.CreateAsyncScope();
        var keys = await scope.ServiceProvider.GetRequiredService<KekeBeautyDbContext>().DataProtectionKeys.AsNoTracking().ToListAsync();

        keys.Should().NotBeEmpty();
        keys.Should().OnlyContain(key => key.Xml!.Contains("encryptedSecret") && !key.Xml.Contains("unencrypted form"),
            "a copy of the database must not be enough to open every TOTP secret");
    }
}
