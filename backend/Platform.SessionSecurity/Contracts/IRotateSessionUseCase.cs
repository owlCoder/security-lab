// IDN-11 TIM 09
namespace Platform.SessionSecurity.Contracts;

/// <summary>
/// IDN-11: replaces the session identifier after a login or a change of the privilege level.
/// After a successful rotation the previous identifier is rejected.
/// </summary>
/// <remarks>
/// <para>
/// Order: the rotation is committed in the store first and audited after that, because only then
/// is the real outcome known. A successful rotation is reported only when the audit record was
/// written (AUD-10, fail-closed).
/// </para>
/// <para>
/// When the audit write fails after a committed rotation, the implementation revokes the whole
/// chain with <see cref="ISessionRotationStore.RevokeChainAsync"/> before it returns
/// <c>AuditFailed</c>. This compensation is not cancelled by the caller's cancellation token.
/// The resulting state is consistent: the old session and its replacement are both revoked, no
/// session of the chain is active, and the client has to authenticate again. The old token never
/// becomes valid again, and the new token never leaves the server.
/// </para>
/// <para>
/// If the compensation itself fails (the store throws), the exception is propagated and no token
/// is returned. The replacement then stays active, but nobody holds its token, and it ends at the
/// absolute expiry of the chain at the latest. The host treats this as a failed rotation.
/// </para>
/// </remarks>
public interface IRotateSessionUseCase
{
    ValueTask<SessionRotationResult> RotateAsync(
        RotateSessionRequest? request,
        CancellationToken cancellationToken = default);
}
