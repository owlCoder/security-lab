using Platform.Identity.Enums;

namespace Platform.Api.Interfaces.Identity;

public interface IMfaPolicy
{
    bool IsRequiredFor(AccountRole role);
}
