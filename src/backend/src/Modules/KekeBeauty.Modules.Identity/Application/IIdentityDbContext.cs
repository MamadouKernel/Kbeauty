using KekeBeauty.Modules.Identity.Domain;
using Microsoft.EntityFrameworkCore;

namespace KekeBeauty.Modules.Identity.Application;

/// <summary>
/// Persistence port of the Identity module. The module never references the Infrastructure project;
/// the shared <c>KekeBeautyDbContext</c> implements this interface.
/// </summary>
public interface IIdentityDbContext
{
    DbSet<User> Users { get; }
    DbSet<RefreshToken> RefreshTokens { get; }
    DbSet<EmailToken> EmailTokens { get; }
    DbSet<LoginChallenge> LoginChallenges { get; }
    DbSet<RecoveryCode> RecoveryCodes { get; }
    DbSet<PhoneCode> PhoneCodes { get; }
    DbSet<TrustedDevice> TrustedDevices { get; }

    Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);
}
