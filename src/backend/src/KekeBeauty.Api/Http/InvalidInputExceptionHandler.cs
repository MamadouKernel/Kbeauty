using Microsoft.AspNetCore.Diagnostics;
using Npgsql;

namespace KekeBeauty.Api.Http;

/// <summary>
/// A NUL byte (<c>\0</c>) in user input (a search term, an e-mail, a note…) is rejected by PostgreSQL with SQLSTATE 22021
/// ("invalid byte sequence for encoding UTF8"). Left unhandled it surfaces as a 500 with a trace id — an error leak and a
/// cheap way to make the server throw. Treat it for what it is: malformed input, answered with a 400.
/// </summary>
public sealed class InvalidInputExceptionHandler(IProblemDetailsService problemDetails) : IExceptionHandler
{
    private const string InvalidTextRepresentation = "22021";

    public async ValueTask<bool> TryHandleAsync(HttpContext httpContext, Exception exception, CancellationToken cancellationToken)
    {
        if (FindPostgresInputError(exception) is null)
        {
            return false;
        }

        httpContext.Response.StatusCode = StatusCodes.Status400BadRequest;
        return await problemDetails.TryWriteAsync(new ProblemDetailsContext
        {
            HttpContext = httpContext,
            ProblemDetails =
            {
                Status = StatusCodes.Status400BadRequest,
                Title = "Requête invalide : caractère non autorisé dans les données envoyées.",
            },
        });
    }

    private static PostgresException? FindPostgresInputError(Exception? exception)
    {
        for (var current = exception; current is not null; current = current.InnerException)
        {
            if (current is PostgresException { SqlState: InvalidTextRepresentation } postgres)
            {
                return postgres;
            }
        }

        return null;
    }
}
