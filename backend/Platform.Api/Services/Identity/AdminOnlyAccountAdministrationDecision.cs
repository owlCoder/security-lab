using Platform.Api.Interfaces.Identity;
using Platform.Api.Models.Identity;

namespace Platform.Api.Services.Identity;

/// Samo administrator sme da menja status. Sve ostalo je zabranjeno.
public sealed class AdminOnlyAccessDecision(IAdminDirectory admins) : IAccessDecision
{
    public bool CanChangeAccountStatus(CallerIdentity caller) =>
        caller.IsAuthenticated && admins.IsAdministrator(caller.UserId ?? Guid.Empty);

    // IDN-05: same administrator source as the REV-05 account.unlock permission (IAdminDirectory).
    public bool CanUnlockAccount(CallerIdentity caller) =>
        caller.IsAuthenticated && admins.IsAdministrator(caller.UserId ?? Guid.Empty);
}
