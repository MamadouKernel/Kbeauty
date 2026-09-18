namespace KekeBeauty.Application.Auth;

public interface IOtpSender
{
    Task<bool> SendAsync(string telephone, string code, CancellationToken cancellationToken);
}
