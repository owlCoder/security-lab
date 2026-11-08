using Platform.Api.Enums.Identity;
using Platform.Api.Middleware;
using Platform.Api.Models.Identity;
using Platform.Api.Security.RevisionAuthorization;
using Platform.Audit.Contracts;

namespace Platform.Api.Modules.Audit;

/// <summary>
/// AUD-01: tells the ordinary <see cref="IAuditWriter"/> who the caller is. Uses ONLY the session
/// that the server validated (never a body, query string or header). A request without an
/// active session is recorded as Anonymous; outside a request there is no actor at all.
/// </summary>
public sealed class SessionAuditActorSource(IHttpContextAccessor accessor) : IAuditActorSource
{
    public AuditActor? GetCurrentActor()
    {
        var http = accessor.HttpContext;
        if (http is null)
            return null;

        var session = http.Items[RevisionRequestIdentity.SessionItemKey] as ValidateSession
            ?? http.Items[SessionCheckMiddleware.SessionItemKey] as ValidateSession;

        return session is { Level: AuthenticationLevel.Active } && session.UserId != Guid.Empty
            ? AuditActor.ForUser(session.UserId)
            : AuditActor.Anonymous;
    }
}
