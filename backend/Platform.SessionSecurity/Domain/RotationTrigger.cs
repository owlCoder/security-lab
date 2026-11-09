// IDN-11 TIM 09
namespace Platform.SessionSecurity.Domain;

/// <summary>
/// IDN-11: events after which the session identifier must be replaced.
/// Each value is a change of the privilege level of the session.
/// </summary>
public enum RotationTrigger
{
    /// <summary>Successful login (protection against session fixation).</summary>
    Login = 0,

    /// <summary>The second factor was completed (IDN-06).</summary>
    MfaCompleted = 1,

    /// <summary>The user confirmed their identity again before a sensitive change.</summary>
    ReauthenticatedForSensitiveChange = 2,

    /// <summary>A role that affects the active session was changed.</summary>
    RoleChanged = 3
}
