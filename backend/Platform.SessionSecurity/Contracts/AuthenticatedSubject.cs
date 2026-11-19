// IDN-22 TIM 09
namespace Platform.SessionSecurity.Contracts;

/// <summary>
/// SEC-01: the caller as confirmed by the server from a checked session. The host builds it;
/// it is never read from the request body, the query string or a header sent by the client.
/// </summary>
public sealed record AuthenticatedSubject(string SubjectId, bool IsAuthenticated);
