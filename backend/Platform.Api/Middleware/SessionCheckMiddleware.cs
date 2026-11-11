using Microsoft.AspNetCore.Authorization;
using Platform.Api.Interfaces.Identity;
using Platform.Api.Models.Identity;
using Platform.Api.Services.Identity;

namespace Platform.Api.Middleware;

public class SessionCheckMiddleware
{
    public const string SessionItemKey = "session";
    public const string InvalidSessionCode = "session_invalid";

    private readonly RequestDelegate _next;
    public SessionCheckMiddleware(RequestDelegate next)
    {
        _next = next;
    }

    public async Task Invoke(HttpContext context, ISessionManager sessionManager)
    {
        string? token = context.Request.Cookies[SessionCookie.Name];
        var endpoint = context.GetEndpoint();

        if(endpoint != null)
        {
            var allowAnonymous = endpoint.Metadata.GetMetadata<AllowAnonymousAttribute>();

            if(allowAnonymous != null)
            {
                await _next(context);
                return;
            }
        }

        if(endpoint == null)
        {
            await _next(context);
            return;
        }

        // Development-only routes of other modules that authenticate callers themselves or are anonymous demos:
        // /api/laboratories and its /api/labs alias (LAB-01 test header), /api/kt1 (ACC-04/ADM-08 synthetic KT1 cookie)
        // and /api/access/demo-deny (Access default-deny demo).
        if (context.RequestServices is not null
            && context.RequestServices.GetService<IHostEnvironment>()?.IsDevelopment() == true
            && (context.Request.Path.StartsWithSegments("/api/laboratories")
                || (context.Request.Path.StartsWithSegments("/api/labs")
                && context.Request.Path.Value?.TrimEnd('/').EndsWith("/student-view", StringComparison.OrdinalIgnoreCase) != true
                // AUT-07: pregled pre objave takođe traži pravu sesiju (LabCaller), i u Developmentu.
                && context.Request.Path.Value?.TrimEnd('/').EndsWith("/preview", StringComparison.OrdinalIgnoreCase) != true)
                || context.Request.Path.StartsWithSegments("/api/kt1")
                || context.Request.Path.StartsWithSegments("/api/access/demo-deny")))
        {
            await _next(context);
            return;
        }

        if (string.IsNullOrEmpty(token))
        {
            context.Response.StatusCode = 401;
            await WriteInvalidSessionResponse(context, clearCookie: false);
            return;
        }

        ValidateSession? validate = await sessionManager.ValidateAsync(token, context.RequestAborted);

        if (validate == null)
        {
            context.Response.StatusCode = 401;
            await WriteInvalidSessionResponse(context, clearCookie: true);
            return;
        }

        context.Items[SessionItemKey] = validate;
        await _next(context);
    }

    // IDN-07: the client gets a stable code to send the user to the login page,
    // and the rejected cookie is removed so no tab sends it again.
    private static async Task WriteInvalidSessionResponse(HttpContext context, bool clearCookie)
    {
        context.Response.Headers.CacheControl = "no-store";

        if (clearCookie)
            context.Response.Cookies.Delete(SessionCookie.Name, SessionCookie.Options(DateTimeOffset.UnixEpoch));

        await context.Response.WriteAsJsonAsync(new { code = InvalidSessionCode });
    }
}
