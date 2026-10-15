using Google.Apis.Auth;
using KekeBeauty.Modules.Identity.Application;
using Microsoft.Extensions.Options;

namespace KekeBeauty.Modules.Identity.Infrastructure;

/// <summary>Validates a Google Identity Services ID token (signature, expiry, issuer, audience = our client ID).</summary>
public sealed class GoogleIdTokenVerifier(IOptions<AuthOptions> options) : IGoogleIdTokenVerifier
{
    public async Task<GoogleIdentity?> VerifyAsync(string idToken, CancellationToken cancellationToken = default)
    {
        var clientId = options.Value.Google.ClientId;
        if (string.IsNullOrWhiteSpace(clientId))
        {
            return null;
        }

        try
        {
            var payload = await GoogleJsonWebSignature.ValidateAsync(
                idToken,
                new GoogleJsonWebSignature.ValidationSettings { Audience = [clientId] });

            return new GoogleIdentity(payload.Subject, payload.Email, payload.EmailVerified, payload.GivenName ?? string.Empty, payload.FamilyName ?? string.Empty);
        }
        catch (InvalidJwtException)
        {
            return null;
        }
    }
}
