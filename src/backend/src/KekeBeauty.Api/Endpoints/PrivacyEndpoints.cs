using System.Security.Claims;
using System.Text.Json;
using KekeBeauty.Api.Http;
using KekeBeauty.Infrastructure.Privacy;
using Microsoft.AspNetCore.Mvc;

namespace KekeBeauty.Api.Endpoints;

/// <summary>Personal data rights (law no. 2013-450, ARTCI): export what we hold, erase the account.</summary>
public static class PrivacyEndpoints
{
    public static IEndpointRouteBuilder MapPrivacyEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1/me").WithTags("Privacy").RequireAuthorization();

        group.MapGet("/export", async (ClaimsPrincipal user, HttpContext http, PrivacyService service, CancellationToken ct) =>
            {
                var result = await service.ExportAsync(user.UserId(), http.Connection.RemoteIpAddress?.ToString(), ct);
                if (result.IsFailure)
                {
                    return ProblemResults.From(result.Error);
                }

                http.Response.Headers.CacheControl = "no-store";
                return Results.File(
                    JsonSerializer.SerializeToUtf8Bytes(result.Value, new JsonSerializerOptions(JsonSerializerDefaults.Web) { WriteIndented = true }),
                    "application/json",
                    "mes-donnees-keke-beauty.json");
            })
            .Produces<PersonalDataExport>().ProducesProblem(StatusCodes.Status404NotFound);

        // POST rather than DELETE: the confirmation password travels in a body, which proxies may drop on DELETE.
        group.MapPost("/delete", async (DeleteAccountInput input, ClaimsPrincipal user, HttpContext http, PrivacyService service, CancellationToken ct) =>
            {
                var result = await service.DeleteAsync(user.UserId(), input.Password, http.Connection.RemoteIpAddress?.ToString(), ct);
                return result.IsSuccess ? Results.NoContent() : ProblemResults.From(result.Error);
            })
            .Produces(StatusCodes.Status204NoContent)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status409Conflict);

        return app;
    }
}
