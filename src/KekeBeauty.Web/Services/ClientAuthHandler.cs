namespace KekeBeauty.Web.Services;

public sealed class ClientAuthHandler(ClientSessionService session) : DelegatingHandler
{
    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        request.Headers.Remove("X-Client-Id");
        var token = await session.GetTokenAsync();
        if (!string.IsNullOrWhiteSpace(token)) request.Headers.TryAddWithoutValidation("X-Client-Token", token);
        return await base.SendAsync(request, cancellationToken);
    }
}