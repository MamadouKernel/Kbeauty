namespace KekeBeauty.Modules.Identity.Application;

public sealed record EmailMessage(string To, string Subject, string TextBody);

/// <summary>E-mail port. V1 ships a logging implementation; a real SMTP sender arrives with the notifications module.</summary>
public interface IEmailSender
{
    Task SendAsync(EmailMessage message, CancellationToken cancellationToken = default);
}

public sealed record GoogleIdentity(string Subject, string Email, bool EmailVerified, string FirstName, string LastName);

/// <summary>Verifies a Google ID token (audience = our OAuth client ID). Null means the token is invalid.</summary>
public interface IGoogleIdTokenVerifier
{
    Task<GoogleIdentity?> VerifyAsync(string idToken, CancellationToken cancellationToken = default);
}
