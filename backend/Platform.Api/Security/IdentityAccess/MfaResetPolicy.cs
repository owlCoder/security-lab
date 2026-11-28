using Platform.Access.Contracts;
using Platform.Access.Domain.Models;
using Platform.Api.Interfaces.Identity;

namespace Platform.Api.Security.IdentityAccess;

/// <summary>
/// US-32: only an administrator may reset the MFA of another account. The administrator source is the same
/// <see cref="IAdminDirectory"/> as for <see cref="AccountStatusChangePolicy"/>. <c>ResetMfa</c> repeats the check
/// as a second line, and the MFA service checks it again.
/// </summary>
public sealed class MfaResetPolicy(IAdminDirectory admins) : IAccessPolicy
{
    public string Id => "identity.account.mfa-reset.admin-only";
    public string Version => "v1";

    public ValueTask<bool> EvaluateAsync(
        AccessRequest request,
        CancellationToken cancellationToken = default)
    {
        var allowed = Guid.TryParse(request.Subject?.Id, out var userId)
            && userId != Guid.Empty
            && admins.IsAdministrator(userId);

        return ValueTask.FromResult(allowed);
    }
}
