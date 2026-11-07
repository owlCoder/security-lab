// IDN-11 TIM 09
namespace Platform.SessionSecurity.Contracts;

/// <summary>IDN-11: result of one atomic <see cref="ISessionRotationStore.RotateAsync"/> call.</summary>
public enum SessionRotationStoreStatus
{
    /// <summary>The old session was revoked and the replacement stored, in one step.</summary>
    Rotated = 0,

    /// <summary>No session has the given token hash.</summary>
    NotFound = 1,

    /// <summary>The old session was already revoked, also by a concurrent rotation.</summary>
    AlreadyRevoked = 2
}
