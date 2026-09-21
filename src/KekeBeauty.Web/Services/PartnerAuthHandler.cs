namespace KekeBeauty.Web.Services;

public sealed class PartnerAuthHandler(PartnerSessionService session) : DelegatingHandler
{
    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        // Une requete ne doit jamais contenir plusieurs X-Partner-Token : ASP.NET les concatene
        // et la signature devient invalide. Respecte aussi un jeton explicitement fourni.
        if (!request.Headers.Contains("X-Partner-Token"))
        {
            var token = await session.GetTokenAsync();
            if (!string.IsNullOrWhiteSpace(token))
                request.Headers.TryAddWithoutValidation("X-Partner-Token", token);
        }
        return await base.SendAsync(request, cancellationToken);
    }
}