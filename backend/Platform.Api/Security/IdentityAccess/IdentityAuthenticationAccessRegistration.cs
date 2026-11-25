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
            .AddAccessAction<SessionReadPolicy>(
                new ActionDefinition(IdentityAccessActions.SessionRead, ActionScope.Authenticated, null, false))
            // IDN-06: enrollment needs a fully authenticated (Active) session; verification is the second login step.
            .AddAccessAction<MfaEnrollmentPolicy>(
                new ActionDefinition(IdentityAccessActions.MfaEnroll, ActionScope.Authenticated, null, false))
            .AddAccessAction<MfaEnrollmentPolicy>(
                new ActionDefinition(IdentityAccessActions.MfaEnrollConfirm, ActionScope.Authenticated, null, false))
            .AddAccessAction<MfaVerificationPolicy>(
                new ActionDefinition(IdentityAccessActions.MfaVerify, ActionScope.Public, null, false));
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

public sealed class SessionReadPolicy : IAccessPolicy
{
    public string Id => IdentityAccessActions.SessionRead;
    public string Version => "v1";

    public ValueTask<bool> EvaluateAsync(AccessRequest request, CancellationToken cancellationToken = default) =>
        ValueTask.FromResult(request.Action == IdentityAccessActions.SessionRead && request.Resource is null &&
            request.Subject is { State: AccountState.Active } &&
            Guid.TryParse(request.Subject.Id, out var userId) && userId != Guid.Empty);
}

/// <summary>
/// IDN-06: only an active, fully signed-in account may start or confirm its own TOTP enrollment. A <c>MfaPending</c>
/// session gives no subject (<see cref="IdentitySessionSubjectProvider"/>), so it is refused here as well.
/// </summary>
public sealed class MfaEnrollmentPolicy : IAccessPolicy
{
    public string Id => "identity.mfa.enrollment";
    public string Version => "v1";

    public ValueTask<bool> EvaluateAsync(AccessRequest request, CancellationToken cancellationToken = default) =>
        ValueTask.FromResult(
            request.Action is IdentityAccessActions.MfaEnroll or IdentityAccessActions.MfaEnrollConfirm &&
            request.Resource is null &&
            request.Subject is { State: AccountState.Active } &&
            Guid.TryParse(request.Subject.Id, out var userId) && userId != Guid.Empty);
}

/// <summary>
/// IDN-06: the TOTP check after the password. Like login it has no Access subject; <c>MfaService.VerifyAsync</c>
/// accepts only an unexpired <c>MfaPending</c> session from the cookie and changes nothing else.
/// </summary>
public sealed class MfaVerificationPolicy : IAccessPolicy
{
    public string Id => "identity.mfa.verification";
    public string Version => "v1";

    public ValueTask<bool> EvaluateAsync(AccessRequest request, CancellationToken cancellationToken = default) =>
        ValueTask.FromResult(request.Action == IdentityAccessActions.MfaVerify && request.Resource is null);
}
