using Microsoft.AspNetCore.Diagnostics;

namespace EnergyMeter.Api.Endpoints;

/// <summary>
/// Turns binding failures (e.g. fromUtc=not-a-date, malformed JSON) into a 400 problem response.
/// Without this, UseExceptionHandler reports them as 500.
/// </summary>
internal sealed class BadRequestExceptionHandler(IProblemDetailsService problemDetails) : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(HttpContext context, Exception exception, CancellationToken ct)
    {
        if (exception is not BadHttpRequestException badRequest)
        {
            return false;
        }

        context.Response.StatusCode = badRequest.StatusCode;
        return await problemDetails.TryWriteAsync(new ProblemDetailsContext
        {
            HttpContext = context,
            Exception = exception,
            ProblemDetails =
            {
                Status = badRequest.StatusCode,
                Title = "The request could not be read.",
                Detail = badRequest.Message
            }
        });
    }
}
