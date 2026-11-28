using Platform.Api.Models.Identity;

namespace Platform.Api.Interfaces.Identity;

public interface IAccessDecision
{
    bool CanChangeAccountStatus(CallerIdentity caller);

    /// <summary>IDN-05: second-line check for an administrator unlock (Access already checked <c>account.unlock</c>).</summary>
    bool CanUnlockAccount(CallerIdentity caller);

    /// <summary>
    /// IDN-06: second-line check for an administrator MFA reset (Access already checked <c>account.mfa.reset</c>).
    /// Default interface method that denies, so existing implementations and test fakes keep compiling
    /// and a new implementation never allows by accident.
    /// </summary>
    bool CanResetMfa(CallerIdentity caller) => false;
}