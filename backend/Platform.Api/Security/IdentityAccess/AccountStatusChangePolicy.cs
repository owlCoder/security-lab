using Platform.Access.Contracts;
using Platform.Access.Domain.Models;
using Platform.Api.Interfaces.Identity;

namespace Platform.Api.Security.IdentityAccess;

/// <summary>
/// IDN-03: only an administrator may change an account status. The administrator source is the same
/// <see cref="IAdminDirectory"/> that <c>AdminOnlyAccessDecision</c> uses, so the set of administrators is
/// unchanged. <c>ChangeAccountStatus</c> repeats the check as a second line.
/// </summary>
public sealed class AccountStatusChangePolicy(IAdminDirectory admins) : IAccessPolicy
{
    public string Id => "identity.account.status.admin-only";
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
