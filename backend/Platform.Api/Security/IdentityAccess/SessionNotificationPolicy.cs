using Platform.Access.Contracts;
using Platform.Access.Domain.Enums;
using Platform.Access.Domain.Models;

namespace Platform.Api.Security.IdentityAccess;

public sealed class SessionNotificationPolicy : IAccessPolicy
{
    public string Id => "identity.session-notifications.self-service";
    public string Version => "v1";

    public ValueTask<bool> EvaluateAsync(
        AccessRequest request,
        CancellationToken cancellationToken = default) =>
        ValueTask.FromResult(
            request.Action is IdentityAccessActions.SessionNotificationsRead
                or IdentityAccessActions.SessionNotificationsUpdate &&
            request.Resource is null &&
            request.Subject is { State: AccountState.Active } &&
            Guid.TryParse(request.Subject.Id, out var userId) && userId != Guid.Empty);
}
