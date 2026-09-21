namespace KekeBeauty.Api.Auth;

public sealed class ClientSessionMiddleware(RequestDelegate next)
{
    public async Task InvokeAsync(HttpContext context, UserSessionTokenService tokens)
    {
        context.Request.Headers.Remove("X-Client-Id");
        var values = context.Request.Headers["X-Client-Token"];
        if (values.Count == 1 && tokens.TryValidate(values[0], "CLIENT", out var userId))
            context.Request.Headers["X-Client-Id"] = userId.ToString();
        await next(context);
    }
}