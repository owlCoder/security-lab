using Microsoft.AspNetCore.Authorization;

namespace Platform.Api.Services.Identity;

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
