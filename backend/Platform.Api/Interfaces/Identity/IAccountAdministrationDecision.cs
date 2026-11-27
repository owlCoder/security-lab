using Platform.Api.Models.Identity;

namespace Platform.Api.Interfaces.Identity;

public interface IAccessDecision
{
    bool CanChangeAccountStatus(CallerIdentity caller);

    /// <summary>IDN-05: second-line check for an administrator unlock (Access already checked <c>account.unlock</c>).</summary>
    bool CanUnlockAccount(CallerIdentity caller);
}