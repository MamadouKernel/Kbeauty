namespace KekeBeauty.Application.Auth;

public interface IEmailSender
{
    Task<bool> SendAsync(string toEmail, string subject, string body, CancellationToken cancellationToken);
}
