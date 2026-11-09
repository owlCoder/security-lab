using Team21.Acc.Core.Interfaces;

namespace Team21.Acc.Api.Services;

/// <summary>
/// Nobody is signed in. Used outside Development until real login is wired in.
/// </summary>
public sealed class AnonymousCurrentActor : ICurrentActor
{
    public Guid? UserId => null;

    public bool IsAuthenticated => false;
}
