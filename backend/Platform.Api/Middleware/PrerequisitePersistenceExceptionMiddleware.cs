using Platform.Api.ExceptionHandling;
using Platform.Prerequisites.Exceptions;

namespace Platform.Api.Middleware;

public sealed class PrerequisitePersistenceExceptionMiddleware(
    RequestDelegate next,
    PrerequisitePersistenceExceptionHandler handler)
{
    public async Task InvokeAsync(HttpContext context)
    {
        try
        {
            await next(context);
        }
        catch (PrerequisitePersistenceUnavailableException exception)
        {
            await handler.TryHandleAsync(
                context,
                exception,
                context.RequestAborted);
        }
    }
}