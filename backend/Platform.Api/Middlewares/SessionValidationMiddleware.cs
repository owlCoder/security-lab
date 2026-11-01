using Platform.Api.Services.Identity;

namespace Platform.Api.Middlewares;

public sealed class SessionValidationMiddleware(
    RequestDelegate next,
    TimeProvider clock,
    IAuditWriter audit)
{
    /// <summary>
    /// Used to reject requests if the state of callers session is invalid
    /// An invalid state of session is if its Date is expired or if the session has been revoked
    /// </summary>
    public async Task InvokeAsync(
        HttpContext context,
        IIdentityContext identity,
        ISessionManager sessions)
    {
        var caller = identity.Current;

        // check if user is authenticated
        if (caller is null || !caller.IsAuthenticated)
        {
            await next(context);
            return;
        }

        // check if user and session exist
        if (caller.UserId is null ||
            caller.CurrentSessionHandle is null ||
            caller.CurrentSessionHandle == Guid.Empty)
        {
            context.Response.StatusCode = StatusCodes.Status401Unauthorized;
            return;
        }

        // check if session is valid
        var isActive = sessions.IsSessionActive(
            caller.UserId.Value,
            caller.CurrentSessionHandle.Value,
            context.RequestAborted);

        // log rejected request and return appropriate status code
        if (!isActive)
        {
            WriteAudit(caller.UserId, context, audit, "session is revoked, expired or does not exist");
            context.Response.StatusCode = StatusCodes.Status401Unauthorized;
            return;
        }

        await next(context);
    }

    // added extra function to audit rejected requests in order to keep main function clean
    private void WriteAudit(
        Guid? userId,
        HttpContext context,
        IAuditWriter audit,
        string detail)
    {
        audit.Write(new AuditEvent(
            Time: clock.GetUtcNow(),
            ActorId: userId,
            Action: "session.validation",
            Target: userId?.ToString() ?? "unknown",
            Outcome: "denied",
            CorrelationId: context.TraceIdentifier,
            Detail: detail
        ));
    }
}