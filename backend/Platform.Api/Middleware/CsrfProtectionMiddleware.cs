using Microsoft.AspNetCore.Antiforgery;
using Platform.Access.AspNet;
using Platform.Api.Interfaces.Identity;
using Platform.Api.Modules.Identity.Federation;
using Platform.Api.Security.IdentityAccess;
using Platform.Api.Services.Identity;

namespace Platform.Api.Middleware;

/// <summary>
/// US-15 / SEC-03: a state-changing request (anything but GET, HEAD, OPTIONS, TRACE) must carry a valid CSRF token
/// when the browser sends it with a valid session, and always on login (login CSRF).
/// It runs right after routing and correlation, before any session check, access decision, rate limit or audit,
/// so a refused request has no side effect: the session is not extended (US-14), no permit is spent.
/// A request without a valid session (no cookie, or a revoked, expired or unknown one) is not refused here: it has
/// no ambient credential to abuse, and the session check further on answers it with <c>401 session_invalid</c>.
/// Logout and the OIDC callback are the documented exceptions.
/// The token is made and validated by ASP.NET Core antiforgery (no custom cryptography) and is bound to the
/// session cookie by <see cref="CsrfSessionBinding"/>.
/// </summary>
public sealed class CsrfProtectionMiddleware(RequestDelegate next, ILogger<CsrfProtectionMiddleware> logger)
{
    /// <summary>What the check requires of one request.</summary>
    public enum Requirement
    {
        /// <summary>Safe method or documented exception.</summary>
        None,

        /// <summary>Login: no session yet, the token is always required (login CSRF).</summary>
        Always,

        /// <summary>The request carries the session cookie: the token is required when the session is valid.</summary>
        WhenSessionIsValid
    }

    public async Task InvokeAsync(HttpContext context, IAntiforgery antiforgery)
    {
        var requirement = RequirementFor(context);
        if (requirement == Requirement.None)
        {
            await next(context);
            return;
        }

        try
        {
            await antiforgery.ValidateRequestAsync(context);
        }
        catch (AntiforgeryValidationException)
        {
            // Without a valid session there is nothing to forge: the session check answers it (401), as before.
            if (requirement == Requirement.WhenSessionIsValid && !await HasValidSessionAsync(context))
            {
                await next(context);
                return;
            }

            // AUD-03: never log the token or the cookie, only that the check failed.
            logger.LogInformation(
                "CSRF token missing or invalid for {Method} {Endpoint}. CorrelationId: {CorrelationId}",
                context.Request.Method,
                context.GetEndpoint()?.DisplayName ?? "unknown endpoint",
                context.TraceIdentifier);

            context.Response.StatusCode = StatusCodes.Status403Forbidden;
            context.Response.Headers.CacheControl = "no-store";
            await context.Response.WriteAsJsonAsync(new { code = CsrfToken.InvalidCode });
            return;
        }

        await next(context);
    }

    public static Requirement RequirementFor(HttpContext context)
    {
        var method = context.Request.Method;
        if (HttpMethods.IsGet(method) || HttpMethods.IsHead(method)
            || HttpMethods.IsOptions(method) || HttpMethods.IsTrace(method))
            return Requirement.None;

        // The OIDC handler answers its callback itself (no endpoint is mapped there). It is a cross-site form POST
        // from the provider, protected by state, nonce and PKCE; with a same-site provider it would carry our cookie.
        // Same comparison as the handler (PathString, ignoring case).
        if (context.Request.Path == OidcFederationExtensions.CallbackPath)
            return Requirement.None;

        var action = context.GetEndpoint()?.Metadata.GetMetadata<IAccessRequirementMetadata>()?.Action;

        if (action == IdentityAccessActions.Logout)
            return Requirement.None;

        if (action == IdentityAccessActions.Login)
            return Requirement.Always;

        return context.Request.Cookies.ContainsKey(SessionCookie.Name)
            ? Requirement.WhenSessionIsValid
            : Requirement.None;
    }

    // Records no activity on the session (PeekAsync). If the answer is unknown, the request is refused (fail closed).
    private async Task<bool> HasValidSessionAsync(HttpContext context)
    {
        var sessions = context.RequestServices.GetService<ISessionManager>();
        if (sessions is null)
            return true;

        try
        {
            return await sessions.PeekAsync(context.Request.Cookies[SessionCookie.Name] ?? string.Empty,
                context.RequestAborted) is not null;
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            logger.LogWarning("CSRF check could not read the session, the request is refused. CorrelationId: {CorrelationId}",
                context.TraceIdentifier);
            return true;
        }
    }
}
