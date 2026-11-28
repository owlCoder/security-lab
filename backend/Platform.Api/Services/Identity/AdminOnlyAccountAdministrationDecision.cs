using Platform.Api.Interfaces.Identity;
using Platform.Api.Models.Identity;

namespace Platform.Api.Services.Identity;

/// <summary>IDN-03 i IDN-05: samo administrator sme da menja status naloga i da ga otključa. Sve ostalo je zabranjeno.</summary>
public sealed class AdminOnlyAccountAdministrationDecision(IAdminDirectory admins) : IAccountAdministrationDecision
{
    public bool CanChangeAccountStatus(CallerIdentity caller) =>
        caller.IsAuthenticated && admins.IsAdministrator(caller.UserId ?? Guid.Empty);

    // IDN-05: same administrator source as the REV-05 account.unlock permission (IAdminDirectory).
    public bool CanUnlockAccount(CallerIdentity caller) =>
        caller.IsAuthenticated && admins.IsAdministrator(caller.UserId ?? Guid.Empty);
}
