using Microsoft.AspNetCore.Mvc;
using Platform.Prerequisites.Exceptions;

namespace Platform.Api.Middleware;

public sealed class PrerequisitePersistenceExceptionMiddleware(
    RequestDelegate next,
    ILogger<PrerequisitePersistenceExceptionMiddleware> logger)
{
    public async Task InvokeAsync(HttpContext context)
    {
        try
        {
            await next(context);
        }
        catch (PrerequisitePersistenceUnavailableException exception)
        {
            logger.LogError(
                exception,
                "Prerequisite persistence is unavailable. TraceId: {TraceId}",
                context.TraceIdentifier);

            if (context.Response.HasStarted)
            {
                throw;
            }

            context.Response.Clear();
            context.Response.StatusCode =
                StatusCodes.Status503ServiceUnavailable;
            context.Response.ContentType = "application/problem+json";
            context.Response.Headers.CacheControl = "no-store";

            var problem = new ProblemDetails
            {
                Status = StatusCodes.Status503ServiceUnavailable,
                Title = "Laboratorije trenutno nisu dostupne",
                Detail = "Došlo je do privremenog problema. Pokušajte ponovo kasnije.",
                Instance = context.Request.Path
            };

            problem.Extensions["traceId"] = context.TraceIdentifier;

            await context.Response.WriteAsJsonAsync(
                problem,
                options: null,
                contentType: "application/problem+json",
                cancellationToken: context.RequestAborted);
        }
    }
}
