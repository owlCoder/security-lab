using Platform.Access.Contracts;
using Platform.Access.Domain.Enums;
using Platform.Access.Domain.Models;

namespace Platform.Api.Security.IdentityAccess;

/// <summary>
/// IDN-07: an active signed-in user may list and revoke their own sessions. The policy grants nothing on
/// other users' sessions: <c>SessionController</c> takes the user id only from the validated session, and
/// <c>ISessionManager.RevokeSession</c> matches the session handle together with that owner.
/// A request that names a resource, or whose subject is not an active account, is denied.
/// </summary>
public sealed class SessionSelfServicePolicy : IAccessPolicy
{
    public string Id => "identity.session.self-service";
    public string Version => "v1";

    public ValueTask<bool> EvaluateAsync(
        AccessRequest request,
        CancellationToken cancellationToken = default) =>
        ValueTask.FromResult(
            request.Action is IdentityAccessActions.SessionList or IdentityAccessActions.SessionRevoke &&
            request.Resource is null &&
            request.Subject is { State: AccountState.Active } &&
            Guid.TryParse(request.Subject.Id, out var userId) && userId != Guid.Empty);
}
