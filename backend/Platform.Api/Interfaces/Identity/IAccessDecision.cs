using Platform.Api.Models.Identity;

namespace Platform.Api.Interfaces.Identity;

public interface IAccessDecision
{
    bool CanChangeAccountStatus(CallerIdentity caller);
}