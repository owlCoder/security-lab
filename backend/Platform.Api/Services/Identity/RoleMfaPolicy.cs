using Platform.Api.Interfaces.Identity;
using Platform.Identity.Enums;

namespace Platform.Api.Services.Identity;

public sealed class RoleMfaPolicy(IConfiguration configuration) : IMfaPolicy
{
    
    public bool IsRequiredFor(AccountRole role)
    {
        var requiredRoles = configuration.GetSection("Identity:Mfa:RequiredForRoles").Get<string[]>() ?? [];

        if(requiredRoles.Contains(role.ToString()))
        {
            return true;
        }

        return false;
    }
}
