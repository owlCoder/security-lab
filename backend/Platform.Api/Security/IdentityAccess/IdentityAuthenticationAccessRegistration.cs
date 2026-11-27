using Platform.Access.Composition;
using Platform.Access.Contracts;
using Platform.Access.Domain.Enums;
using Platform.Access.Domain.Models;

namespace Platform.Api.Security.IdentityAccess;

public static class IdentityAuthenticationAccessRegistration
{
    /// <summary>Register authentication actions and policies before AddAccessControl().</summary>
    public static IServiceCollection AddIdentityAuthenticationAccess(this IServiceCollection services) =>
        services
            .AddAccessAction<PublicAuthenticationPolicy>(
                new ActionDefinition(IdentityAccessActions.Login, ActionScope.Public, null, false))
            .AddAccessAction<PublicAuthenticationPolicy>(
                new ActionDefinition(IdentityAccessActions.Logout, ActionScope.Public, null, false))
            .AddAccessAction<CsrfTokenPolicy>(
                new ActionDefinition(IdentityAccessActions.CsrfToken, ActionScope.Public, null, false))
            .AddAccessAction<SessionReadPolicy>(
                new ActionDefinition(IdentityAccessActions.SessionRead, ActionScope.Authenticated, null, false));
}

/// <summary>
/// Login checks credentials in Identity. Logout is idempotent even without a valid session;
/// its handler can revoke only the session identified by the supplied cookie.
/// </summary>
public sealed class PublicAuthenticationPolicy : IAccessPolicy
{
    public string Id => "identity.public-authentication";
    public string Version => "v1";

    public ValueTask<bool> EvaluateAsync(AccessRequest request, CancellationToken cancellationToken = default) =>
        ValueTask.FromResult(request.Resource is null &&
            request.Action is IdentityAccessActions.Login or IdentityAccessActions.Logout);
}

/// <summary>US-15: the CSRF token is public. It is useless without the paired cookie, which only our origin can read.</summary>
public sealed class CsrfTokenPolicy : IAccessPolicy
{
    public string Id => IdentityAccessActions.CsrfToken;
    public string Version => "v1";

    public ValueTask<bool> EvaluateAsync(AccessRequest request, CancellationToken cancellationToken = default) =>
        ValueTask.FromResult(request.Action == IdentityAccessActions.CsrfToken && request.Resource is null);
}

public sealed class SessionReadPolicy : IAccessPolicy
{
    public string Id => IdentityAccessActions.SessionRead;
    public string Version => "v1";

    public ValueTask<bool> EvaluateAsync(AccessRequest request, CancellationToken cancellationToken = default) =>
        ValueTask.FromResult(request.Action == IdentityAccessActions.SessionRead && request.Resource is null &&
            request.Subject is { State: AccountState.Active } &&
            Guid.TryParse(request.Subject.Id, out var userId) && userId != Guid.Empty);
}
