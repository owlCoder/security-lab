using Platform.Api.Enums.Identity;
using Platform.Api.Middleware;
using Platform.Api.Models.Identity;
using Platform.Audit.Contracts;

namespace Platform.Api.Security.RevisionAuthorization;

/// <summary>Uses only the server-validated session and server correlation middleware.</summary>
public sealed class RevisionAuditContextProvider(IHttpContextAccessor accessor) : IRevisionAuditContextProvider
{
    public AuditContext GetContext()
    {
        var http = accessor.HttpContext
            ?? throw new InvalidOperationException("REV-05 audit requires an HTTP request context.");
        var session = http.Items[SessionCheckMiddleware.SessionItemKey] as ValidateSession
            ?? http.Items[RevisionRequestIdentity.SessionItemKey] as ValidateSession;
        var actor = session is { Level: AuthenticationLevel.Active } && session.UserId != Guid.Empty
            ? AuditActor.ForUser(session.UserId) : AuditActor.Anonymous;
        return new AuditContext(actor, http.TraceIdentifier);
    }
}
