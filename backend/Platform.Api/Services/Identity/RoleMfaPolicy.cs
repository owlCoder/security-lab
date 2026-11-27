using Microsoft.Extensions.Options;
using Platform.Api.Interfaces.Identity;
using Platform.Identity.Enums;

namespace Platform.Api.Services.Identity;

public sealed class RoleMfaPolicy(IOptions<MfaPolicyOptions> options) : IMfaPolicy
{
    public bool IsRequiredFor(AccountRole role) =>
        options.Value.RequiredForRoles.Contains(role.ToString());
}
