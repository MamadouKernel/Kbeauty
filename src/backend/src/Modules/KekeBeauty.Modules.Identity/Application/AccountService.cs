using KekeBeauty.Modules.Identity.Domain;
using KekeBeauty.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace KekeBeauty.Modules.Identity.Application;

/// <summary>Profile and session management for the signed-in user.</summary>
public sealed class AccountService(IIdentityDbContext db, TimeProvider time)
{
    public async Task<UserDto?> GetMeAsync(Guid userId, CancellationToken ct)
    {
        var user = await db.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Id == userId, ct);
        return user is null ? null : AuthService.ToDto(user);
    }

    public async Task<Result<UserDto>> UpdateProfileAsync(Guid userId, UpdateProfileRequest request, CancellationToken ct)
    {
        var firstName = request.FirstName?.Trim() ?? string.Empty;
        var lastName = request.LastName?.Trim() ?? string.Empty;
        if (firstName.Length is 0 or > 100 || lastName.Length is 0 or > 100)
        {
            return Result.Failure<UserDto>(AuthErrors.InvalidInput);
        }

        var user = await db.Users.FirstOrDefaultAsync(u => u.Id == userId, ct);
        if (user is null)
        {
            return Result.Failure<UserDto>(AuthErrors.InvalidToken);
        }

        user.UpdateProfile(firstName, lastName);
        await db.SaveChangesAsync(ct);
        return Result.Success(AuthService.ToDto(user));
    }

    public async Task<IReadOnlyList<SessionDto>> ListSessionsAsync(Guid userId, Guid? currentSessionId, CancellationToken ct)
    {
        var now = time.GetUtcNow();
        var active = await db.RefreshTokens.AsNoTracking()
            .Where(t => t.UserId == userId && t.RevokedAt == null && t.ExpiresAt > now)
            .OrderByDescending(t => t.SessionStartedAt)
            .ToListAsync(ct);

        return active
            .Select(t => new SessionDto(t.SessionId, t.SessionStartedAt, t.DeviceInfo, t.IpAddress, t.SessionId == currentSessionId))
            .ToList();
    }

    public async Task<bool> RevokeSessionAsync(Guid userId, Guid sessionId, CancellationToken ct)
    {
        var tokensOfSession = await db.RefreshTokens.Where(t => t.UserId == userId && t.SessionId == sessionId && t.RevokedAt == null).ToListAsync(ct);
        if (tokensOfSession.Count == 0)
        {
            return false;
        }

        var now = time.GetUtcNow();
        foreach (var token in tokensOfSession)
        {
            token.Revoke(now);
        }

        await db.SaveChangesAsync(ct);
        return true;
    }
}
