using Platform.Access.AspNet;
using Platform.Access.Domain.Enums;
using Platform.Access.Domain.Models;
using Platform.Api.Enums.Identity;
using Platform.Api.Interfaces.Identity;
using Platform.Api.Services.Identity;

namespace Platform.Api.Security.IdentityAccess;

/// <summary>
/// IDN-03: the central Access middleware gets its subject from the server-side session,
/// the same one <c>SessionCheckMiddleware</c> validates later. Only routes whose RequireAccess action is in
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
        var session = await sessions.ValidateAsync(token, cancellationToken);

        return session is { Level: AuthenticationLevel.Active } && session.UserId != Guid.Empty
            ? new SubjectRef(session.UserId.ToString("D"), AccountState.Active)
            : null;
    }
}
