using Platform.SessionSecurity.Contracts;

// IDN-11 TIM 09
namespace Platform.Tests.SessionSecurity.TestDoubles;

/// <summary>
/// Passes every call to a real store and injects one failure at a time, so tests can drive the
/// failure paths of a rotation: a session that expires between the check and the rotation, a
/// chain revoked before activation, and store errors during activation or chain revocation.
/// With no fault set it behaves exactly like the inner store.
/// </summary>
public sealed class FaultInjectingSessionRotationStore(ISessionRotationStore inner) : ISessionRotationStore
{
    public bool ReportExpiredOnRotate { get; set; }

    public bool RevokeChainBeforeActivate { get; set; }

    public bool ThrowOnActivate { get; set; }

    public bool ThrowOnRevokeChain { get; set; }

    public int RevokeChainCalls { get; private set; }

    public ValueTask<RotatableSession?> FindAsync(
        string tokenHash,
        CancellationToken cancellationToken = default)
        => inner.FindAsync(tokenHash, cancellationToken);

    public ValueTask<SessionRotationStoreStatus> RotateAsync(
        string oldTokenHash,
        RotatableSession replacement,
        DateTimeOffset now,
        CancellationToken cancellationToken = default)
        => ReportExpiredOnRotate
            ? ValueTask.FromResult(SessionRotationStoreStatus.Expired)
            : inner.RotateAsync(oldTokenHash, replacement, now, cancellationToken);

    public async ValueTask<bool> ActivateAsync(
        string tokenHash,
        DateTimeOffset now,
        CancellationToken cancellationToken = default)
    {
        if (ThrowOnActivate)
        {
            throw new InvalidOperationException("Simulated store failure during activation.");
        }

        if (RevokeChainBeforeActivate && await inner.FindAsync(tokenHash, cancellationToken) is { } pending)
        {
            await inner.RevokeChainAsync(pending.ChainId, now, cancellationToken);
        }

        return await inner.ActivateAsync(tokenHash, now, cancellationToken);
    }

    public ValueTask<int> RevokeChainAsync(
        Guid chainId,
        DateTimeOffset now,
        CancellationToken cancellationToken = default)
    {
        RevokeChainCalls++;
        return ThrowOnRevokeChain
            ? throw new InvalidOperationException("Simulated store failure during chain revocation.")
            : inner.RevokeChainAsync(chainId, now, cancellationToken);
    }

    public ValueTask<int> RevokeAllForSubjectAsync(
        string subjectId,
        DateTimeOffset now,
        CancellationToken cancellationToken = default)
        => inner.RevokeAllForSubjectAsync(subjectId, now, cancellationToken);
}
