using Platform.Api.Enums.Identity;
using Platform.Api.Models.Identity;

namespace Platform.Api.Interfaces.Identity;

public interface IMfaService
{
    Task<(MfaOutcome Outcome, string? Uri)> EnrollAsync(
        string? rawToken,
        string correlationId,
        CancellationToken ct);

    Task<MfaOutcome> ConfirmEnrollAsync(
        string? rawToken,
        string? code,
        string correlationId,
        CancellationToken ct);

    /// <summary>
    /// US-18: on success the <c>MfaPending</c> session of <paramref name="rawToken"/> is revoked and
    /// <c>Session</c> is the new <c>Active</c> session for the cookie (IDN-11). On every other outcome it is null.
    /// </summary>
    Task<(MfaOutcome Outcome, IssuedSession? Session)> VerifyAsync(
        string? rawToken,
        string? code,
        string correlationId,
        CancellationToken ct);
}