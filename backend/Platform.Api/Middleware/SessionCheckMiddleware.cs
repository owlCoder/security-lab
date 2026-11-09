using Microsoft.AspNetCore.Authorization;
using Platform.Api.Interfaces.Identity;
using Platform.Api.Models.Identity;
using Platform.Api.Services.Identity;

namespace Platform.Api.Middleware;

public class SessionCheckMiddleware
{
    public const string SessionItemKey = "session";

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
        // /api/access/demo-deny (Access default-deny demo), and /api/courses with /api/materials (Team 21 test header).
        if (context.RequestServices is not null
            && context.RequestServices.GetService<IHostEnvironment>()?.IsDevelopment() == true
            && (context.Request.Path.StartsWithSegments("/api/laboratories")
                || (context.Request.Path.StartsWithSegments("/api/labs")
                && context.Request.Path.Value?.TrimEnd('/').EndsWith("/student-view", StringComparison.OrdinalIgnoreCase) != true
                // AUT-07: pregled pre objave takođe traži pravu sesiju (LabCaller), i u Developmentu.
                && context.Request.Path.Value?.TrimEnd('/').EndsWith("/preview", StringComparison.OrdinalIgnoreCase) != true)
                || context.Request.Path.StartsWithSegments("/api/kt1")
                || context.Request.Path.StartsWithSegments("/api/courses")
                || context.Request.Path.StartsWithSegments("/api/materials")
                || context.Request.Path.StartsWithSegments("/api/access/demo-deny")))
        {
            await _next(context);
            return;
        }

        if (string.IsNullOrEmpty(token))
        {
            context.Response.StatusCode = 401;
            return;
        }

        ValidateSession? validate = await sessionManager.ValidateAsync(token);

        if (validate == null)
        {
            context.Response.StatusCode = 401;
            return;
        }

        context.Items.Add(SessionItemKey, validate);
        await _next(context);
    }
}
