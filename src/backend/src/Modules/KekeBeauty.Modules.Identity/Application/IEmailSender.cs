namespace KekeBeauty.Modules.Identity.Application;

public sealed record EmailMessage(string To, string Subject, string TextBody);

/// <summary>E-mail port. V1 ships a logging implementation; a real SMTP sender arrives with the notifications module.</summary>
public interface IEmailSender
{
    Task SendAsync(EmailMessage message, CancellationToken cancellationToken = default);

    /// <summary>
    /// Like <see cref="SendAsync"/> but says whether the relay accepted the message, so a notification can be recorded as sent or failed.
    /// Senders that cannot tell (logging, third-party campaigns) report success once the call returns.
    /// </summary>
    async Task<bool> TrySendAsync(EmailMessage message, CancellationToken cancellationToken = default)
    {
        await SendAsync(message, cancellationToken);
        return true;
    }
}

public sealed record GoogleIdentity(string Subject, string Email, bool EmailVerified, string FirstName, string LastName);

/// <summary>Verifies a Google ID token (audience = our OAuth client ID). Null means the token is invalid.</summary>
public interface IGoogleIdTokenVerifier
{
    Task<GoogleIdentity?> VerifyAsync(string idToken, CancellationToken cancellationToken = default);
}
