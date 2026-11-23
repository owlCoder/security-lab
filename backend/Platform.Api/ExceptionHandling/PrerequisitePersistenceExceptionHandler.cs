using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;
using Platform.Prerequisites.Exceptions;

namespace Platform.Api.ExceptionHandling;

public sealed class PrerequisitePersistenceExceptionHandler(
    ILogger<PrerequisitePersistenceExceptionHandler> logger) : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(
        HttpContext httpContext,
        Exception exception,
        CancellationToken cancellationToken)
    {
        if (exception is not PrerequisitePersistenceUnavailableException)
        {
            return false;
        }

        logger.LogError(
            exception,
            "Prerequisite persistence is unavailable. TraceId: {TraceId}",
            httpContext.TraceIdentifier);

        var problem = new ProblemDetails
        {
            Status = StatusCodes.Status503ServiceUnavailable,
            Title = "Laboratorije trenutno nisu dostupne",
            Detail = "Došlo je do privremenog problema. Pokušajte ponovo kasnije.",
            Instance = httpContext.Request.Path
        };

        httpContext.Response.StatusCode =
            StatusCodes.Status503ServiceUnavailable;

        await httpContext.Response.WriteAsJsonAsync(
            problem,
            options: null,
            contentType: "application/problem+json",
            cancellationToken: cancellationToken);

        return true;
    }
}