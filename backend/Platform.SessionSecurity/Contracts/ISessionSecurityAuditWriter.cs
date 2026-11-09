// IDN-11 TIM 09
namespace Platform.SessionSecurity.Contracts;

/// <summary>
/// AUD-01: writes session security audit records. Returns false when the record could not be
/// stored; a rotation is then not reported as successful (fail-closed), and the rotated chain is
/// revoked as described on <see cref="IRotateSessionUseCase"/>. The adapter to the Audit module
/// (Team 12) is planned integration work.
/// </summary>
public interface ISessionSecurityAuditWriter
{
    ValueTask<bool> TryWriteAsync(
        SessionSecurityAuditEntry entry,
        CancellationToken cancellationToken = default);
}
