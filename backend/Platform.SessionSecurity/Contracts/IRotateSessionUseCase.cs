// IDN-11 TIM 09
namespace Platform.SessionSecurity.Contracts;

/// <summary>
/// IDN-11: replaces the session identifier after a login or a change of the privilege level.
/// After a successful rotation the previous identifier is rejected.
/// </summary>
/// <remarks>
/// <para>
/// Order (AUD-10, fail-closed): the store first revokes the old session and stores the replacement
/// as pending activation (<see cref="ISessionRotationStore.RotateAsync"/>); then the rotation is
/// audited; only after a successful audit is the replacement activated
/// (<see cref="ISessionRotationStore.ActivateAsync"/>) and the new token returned.
/// </para>
/// <para>
/// Any failure before the activation leaves no active session in the chain, whatever its form:
/// the audit writer returns false or throws, the request is cancelled, or the activation is
/// refused or throws. The old session is already revoked and the replacement was never
/// activated, so the client has to authenticate again; the old token never becomes valid again
/// and the new token never leaves the server. An audit failure gives <c>AuditFailed</c>, an
/// activation refused by the store gives <c>RotationConflict</c>, and cancellation and store
/// exceptions are propagated to the caller.
/// </para>
/// <para>
/// After such a failure the implementation also tries to revoke the whole chain with
/// <see cref="ISessionRotationStore.RevokeChainAsync"/>, independent of the caller's cancellation.
/// This is cleanup only: if the revocation fails too, the pending replacement still cannot be
/// used and ends at the absolute expiry of the chain.
/// </para>
/// </remarks>
public interface IRotateSessionUseCase
{
    ValueTask<SessionRotationResult> RotateAsync(
        RotateSessionRequest? request,
        CancellationToken cancellationToken = default);
}
