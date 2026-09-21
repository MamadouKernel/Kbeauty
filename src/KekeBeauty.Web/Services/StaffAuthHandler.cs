namespace KekeBeauty.Web.Services;

public sealed class StaffAuthHandler(StaffSessionService session) : DelegatingHandler
{
    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        request.Headers.Remove("X-Collaborateur-Id");
        var token = await session.GetTokenAsync();
        if (!string.IsNullOrWhiteSpace(token)) request.Headers.TryAddWithoutValidation("X-Staff-Token", token);
        return await base.SendAsync(request, cancellationToken);
    }
}