// IDN-11 TIM 09
namespace Platform.SessionSecurity.Contracts;

/// <summary>
/// AUD-01: writes session security audit records. Returns false when the record could not be
/// stored. An adapter may also report a failure by throwing; callers treat both the same way, so
/// a rotation is then not reported as successful and its replacement is never activated
/// (fail-closed, see <see cref="IRotateSessionUseCase"/>). The adapter to the Audit module
/// (Team 12) is planned integration work.
/// </summary>
public interface ISessionSecurityAuditWriter
{
    ValueTask<bool> TryWriteAsync(
        SessionSecurityAuditEntry entry,
        CancellationToken cancellationToken = default);
}
