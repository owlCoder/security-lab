using Platform.Api.Enums.Identity;
using Platform.Api.Interfaces.Identity;
using Team21.Acc.Core.Interfaces;

namespace Platform.Api.Modules.Team21.Integration;

/// <summary>
/// Maps the platform's server-validated identity to the Team 21 caller contract.
/// Session validation remains the identity module's responsibility.
/// </summary>
public sealed class SessionCurrentActor : ICurrentActor
{
    private readonly IIdentityContext _identity;

    public SessionCurrentActor(IIdentityContext identity)
    {
        ArgumentNullException.ThrowIfNull(identity);
        _identity = identity;
    }

    public Guid? UserId
    {
        get
        {
            var current = _identity.Current;
            return current is { IsAuthenticated: true, Level: AuthenticationLevel.Active, UserId: { } userId }
                && userId != Guid.Empty
                    ? userId
                    : null;
        }
    }

    public bool IsAuthenticated => UserId is not null;
}
