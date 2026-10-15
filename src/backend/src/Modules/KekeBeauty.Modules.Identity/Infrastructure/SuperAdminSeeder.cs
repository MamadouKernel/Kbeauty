using KekeBeauty.Modules.Identity.Application;
using KekeBeauty.Modules.Identity.Domain;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace KekeBeauty.Modules.Identity.Infrastructure;

/// <summary>
/// Creates the initial SuperAdmin from <c>Admin:Email</c> / <c>Admin:Password</c> (environment variables).
/// Nothing is created when they are absent. The account still has to enrol in 2FA on first login.
/// </summary>
public sealed class SuperAdminSeeder(
    IIdentityDbContext db,
    IPasswordHasher<User> hasher,
    IConfiguration configuration,
    TimeProvider time,
    ILogger<SuperAdminSeeder> logger)
{
    public async Task SeedAsync(CancellationToken cancellationToken)
    {
        var email = (configuration["Admin:Email"] ?? string.Empty).Trim().ToLowerInvariant();
        var password = configuration["Admin:Password"];
        if (email.Length == 0 || string.IsNullOrEmpty(password))
        {
            return;
        }

        if (!PasswordRules.IsValidEmail(email) || PasswordRules.Validate(password, email).Count > 0)
        {
            logger.LogWarning("SuperAdmin not created: Admin:Email is invalid or Admin:Password does not meet the password rules.");
            return;
        }

        if (await db.Users.AnyAsync(u => u.Email == email, cancellationToken))
        {
            return;
        }

        var admin = User.Register(email, "Super", "Admin", AppRoles.SuperAdmin, emailConfirmed: true, time.GetUtcNow());
        admin.SetPassword(hasher.HashPassword(admin, password));
        db.Users.Add(admin);
        await db.SaveChangesAsync(cancellationToken);
        logger.LogInformation("SuperAdmin {Email} created. It must enrol in two-factor authentication at first sign-in.", email);
    }
}
