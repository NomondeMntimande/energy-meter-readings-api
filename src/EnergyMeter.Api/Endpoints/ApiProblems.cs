using EnergyMeter.Core.Time;
using Microsoft.AspNetCore.Http.HttpResults;

namespace EnergyMeter.Api.Endpoints;

/// <summary>Consistent RFC 7807 problem responses across endpoints.</summary>
internal static class ApiProblems
{
    public static ProblemHttpResult NotFound(string resource, long id) =>
        TypedResults.Problem(
            statusCode: StatusCodes.Status404NotFound,
            title: $"{resource} not found",
            detail: $"{resource} with id {id} does not exist.");

    public static ProblemHttpResult Conflict(string title, string detail) =>
        TypedResults.Problem(statusCode: StatusCodes.Status409Conflict, title: title, detail: detail);

    public static ValidationProblem Validation(string field, string message) =>
        TypedResults.ValidationProblem(new Dictionary<string, string[]> { [field] = [message] });

    public static ValidationProblem Validation(Dictionary<string, string[]> errors) =>
        TypedResults.ValidationProblem(errors);

    /// <summary>Returns a validation problem if the range is inverted, otherwise null.</summary>
    public static ValidationProblem? InvalidRange(DateTime? fromUtc, DateTime? toUtc) =>
        fromUtc is { } f && toUtc is { } t && UtcTime.Normalize(f) > UtcTime.Normalize(t)
            ? Validation("fromUtc", "fromUtc must be on or before toUtc.")
            : null;
}
