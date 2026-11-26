using Platform.Access.AspNet;
using Platform.Access.Domain.Enums;
using Platform.Access.Domain.Models;
using Platform.Api.Enums.Identity;
using Platform.Api.Interfaces.Identity;
using Platform.Api.Middleware;
using Platform.Api.Models.Identity;
using Platform.Api.Services.Identity;

namespace Platform.Api.Security.IdentityAccess;

/// <summary>
/// IDN-03: the central Access middleware gets its subject from the server-side session.
/// Uses the result from <c>SessionCheckMiddleware</c> when available. Only routes whose RequireAccess action is in
/// <see cref="IdentityAccessActions.SessionBound"/> are handled here (recognised by metadata, not by path);
/// every other route goes to the inner provider.
/// A missing, unknown, revoked or expired session, a blocked or archived account and a session that is
/// not fully authenticated (MFA pending) all give no subject, so Access answers 401.
/// </summary>
public sealed class IdentitySessionSubjectProvider(IAccessSubjectProvider inner, ISessionManager sessions)
    : IAccessSubjectProvider
{
    public static bool AppliesTo(HttpContext http) =>
        http.GetEndpoint()?.Metadata.GetMetadata<IAccessRequirementMetadata>()?.Action is { } action
        && IdentityAccessActions.SessionBound.Contains(action);

    /// <summary>
    /// IDN-07: a route of the Identity module itself (see <see cref="IdentityAccessActions.IdentityOwned"/>).
    /// <c>SessionCheckMiddleware</c> runs ahead of Access for these, so a bad session gets the stable
    /// <c>401 session_invalid</c> instead of Access's generic 401.
    /// </summary>
    public static bool IsIdentityRoute(HttpContext http) =>
        http.GetEndpoint()?.Metadata.GetMetadata<IAccessRequirementMetadata>()?.Action is { } action
        && IdentityAccessActions.IdentityOwned.Contains(action);

    public async ValueTask<SubjectRef?> GetSubjectAsync(
        HttpContext httpContext,
        CancellationToken cancellationToken = default)
    {
        if (!AppliesTo(httpContext))
            return await inner.GetSubjectAsync(httpContext, cancellationToken);

        var token = httpContext.Request.Cookies[SessionCookie.Name];
        if (string.IsNullOrWhiteSpace(token))
            return null;

        // ValidateAsync also refuses a session of a blocked or archived account (IDN-03).
        var session = httpContext.Items[SessionCheckMiddleware.SessionItemKey] as ValidateSession
            ?? await sessions.ValidateAsync(token, cancellationToken);

        return session is { Level: AuthenticationLevel.Active } && session.UserId != Guid.Empty
            ? new SubjectRef(session.UserId.ToString("D"), AccountState.Active)
            : null;
    }
}
