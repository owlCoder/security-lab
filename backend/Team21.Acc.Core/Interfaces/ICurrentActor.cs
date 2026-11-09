namespace Team21.Acc.Core.Interfaces;

/// <summary>
/// Who is making the request. HeaderCurrentActor in Development, AnonymousCurrentActor everywhere
/// else, until we use the IDN login. Every service checks IsAuthenticated itself.
/// </summary>
public interface ICurrentActor
{
    /// <summary>The caller's account id, or null when nobody is signed in.</summary>
    Guid? UserId { get; }

    bool IsAuthenticated { get; }
}
