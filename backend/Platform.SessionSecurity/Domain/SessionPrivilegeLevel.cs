// IDN-11 TIM 09
namespace Platform.SessionSecurity.Domain;

/// <summary>
/// IDN-11: privilege level of a session. A rotation moves a session to the same or a higher level.
/// </summary>
public enum SessionPrivilegeLevel
{
    /// <summary>The session exists, but no user has logged in with it yet.</summary>
    PreAuthentication = 0,

    /// <summary>The user logged in with the first factor.</summary>
    Authenticated = 1,

    /// <summary>The user completed the second factor or confirmed their identity again.</summary>
    Elevated = 2
}
