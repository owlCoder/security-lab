using Platform.SessionSecurity.Domain;

// IDN-11 TIM 09
namespace Platform.SessionSecurity.Contracts;

/// <summary>
/// IDN-11: request to replace the current session. Built by the host on the server, never
/// copied from the request body.
/// </summary>
/// <param name="CurrentToken">Session token from the client cookie.</param>
/// <param name="SubjectId">
/// User confirmed by the server (for a login: the user whose credentials were just verified).
/// </param>
/// <param name="Trigger">Why the session is rotated.</param>
/// <param name="NewPrivilegeLevel">Privilege level of the new session.</param>
/// <param name="CorrelationId">Request correlation ID (AUD-04).</param>
public sealed record RotateSessionRequest(
    string? CurrentToken,
    string? SubjectId,
    RotationTrigger Trigger,
    SessionPrivilegeLevel NewPrivilegeLevel,
    string? CorrelationId)
{
    // AUD-03: never print the session token.
    public override string ToString() =>
        $"RotateSessionRequest {{ Trigger = {Trigger}, NewPrivilegeLevel = {NewPrivilegeLevel} }}";
}
