using KekeBeauty.SharedKernel;

namespace KekeBeauty.Api.Http;

/// <summary>Maps business <see cref="Error"/> codes to ProblemDetails responses with a stable <c>code</c> extension.</summary>
public static class ProblemResults
{
    public static IResult From(Error error) => Results.Problem(
        title: error.Message,
        statusCode: StatusFor(error.Code),
        extensions: new Dictionary<string, object?> { ["code"] = error.Code });

    public static int StatusFor(string code) => code switch
    {
        _ when code.EndsWith(".not_found", StringComparison.Ordinal) => StatusCodes.Status404NotFound,
        "backup.not_ready" => StatusCodes.Status409Conflict,
        "file.too_large" or "media.video_too_large" => StatusCodes.Status413PayloadTooLarge,
        "plan.feature_required" => StatusCodes.Status402PaymentRequired,
        "admin.forbidden" or "pass.foreign" => StatusCodes.Status403Forbidden,
        "payment.gateway_error" or "payout.gateway_error" => StatusCodes.Status502BadGateway,
        _ when code.EndsWith(".locked", StringComparison.Ordinal)
            || code.EndsWith(".incomplete", StringComparison.Ordinal)
            || code.EndsWith(".limit_reached", StringComparison.Ordinal)
            || code.EndsWith(".last_photo", StringComparison.Ordinal)
            || code.EndsWith(".not_pending", StringComparison.Ordinal)
            || code.EndsWith(".identity_not_valid", StringComparison.Ordinal)
            || code.EndsWith(".closed", StringComparison.Ordinal)
            || code.EndsWith(".taken", StringComparison.Ordinal)
            || code.EndsWith(".unavailable", StringComparison.Ordinal)
            || code.EndsWith(".invalid_transition", StringComparison.Ordinal)
            || code.EndsWith(".too_late", StringComparison.Ordinal)
            || code.EndsWith(".last_active", StringComparison.Ordinal)
            || code.EndsWith(".already_active", StringComparison.Ordinal)
            || code.EndsWith(".self_action", StringComparison.Ordinal)
            || code.EndsWith(".has_establishments", StringComparison.Ordinal)
            || code.EndsWith(".has_upcoming_appointments", StringComparison.Ordinal)
            || code.EndsWith(".already_admin", StringComparison.Ordinal)
            || code.EndsWith(".not_admin", StringComparison.Ordinal)
            || code.EndsWith(".too_soon", StringComparison.Ordinal)
            || code.EndsWith(".nothing_due", StringComparison.Ordinal)
            || code.EndsWith(".already_ended", StringComparison.Ordinal)
            || code.EndsWith(".not_payable", StringComparison.Ordinal)
            || code.EndsWith(".hold_expired", StringComparison.Ordinal)
            || code.EndsWith(".nothing_to_pay", StringComparison.Ordinal)
            || code.EndsWith(".in_progress", StringComparison.Ordinal)
            || code.EndsWith(".not_disputable", StringComparison.Ordinal)
            || code.EndsWith(".already_filed", StringComparison.Ordinal)
            || code.EndsWith(".already_resolved", StringComparison.Ordinal)
            || code.EndsWith(".not_tippable", StringComparison.Ordinal)
            || code.EndsWith(".already_tipped", StringComparison.Ordinal)
            || code.EndsWith(".not_reviewable", StringComparison.Ordinal)
            || code.EndsWith(".already_reviewed", StringComparison.Ordinal)
            || code.EndsWith(".trial_not_offered", StringComparison.Ordinal)
            || code.EndsWith(".trial_used", StringComparison.Ordinal)
            || code.EndsWith(".not_trialing", StringComparison.Ordinal)
            || code.EndsWith(".no_change", StringComparison.Ordinal)
            || code.EndsWith(".not_changeable", StringComparison.Ordinal)
            || code.EndsWith(".no_scheduled_change", StringComparison.Ordinal)
            || code.EndsWith(".not_published", StringComparison.Ordinal) => StatusCodes.Status409Conflict,
        _ => StatusCodes.Status400BadRequest,
    };
}
