using Microsoft.AspNetCore.Diagnostics;

namespace KekeBeauty.Api.Http;

/// <summary>
/// Malformed requests (invalid JSON, wrong content type, ...) are client errors: answer with the status the
/// framework chose (usually 400) instead of letting the generic handler turn them into a 500.
/// </summary>
public sealed class BadRequestExceptionHandler(IProblemDetailsService problemDetails) : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(HttpContext httpContext, Exception exception, CancellationToken cancellationToken)
    {
        if (exception is not BadHttpRequestException badRequest)
        {
            return false;
        }

        httpContext.Response.StatusCode = badRequest.StatusCode;
        return await problemDetails.TryWriteAsync(new ProblemDetailsContext
        {
            HttpContext = httpContext,
            Exception = exception,
            ProblemDetails = { Status = badRequest.StatusCode, Title = "Requête invalide." },
        });
    }
}
