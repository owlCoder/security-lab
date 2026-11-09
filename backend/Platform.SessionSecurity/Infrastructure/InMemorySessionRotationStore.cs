using Platform.SessionSecurity.Contracts;

// IDN-11 TIM 09
namespace Platform.SessionSecurity.Infrastructure;

/// <summary>
/// IDN-11: in-memory session store for tests and the KT1 demo. Every operation runs under one
/// lock, so a rotation is atomic: of several rotations with the same old token hash exactly one
/// succeeds, and the revocation and expiry of the old session are checked inside the same lock. A database adapter replaces this class without changes to the application layer.
/// </summary>
public sealed class InMemorySessionRotationStore : ISessionRotationStore
{
    private readonly object gate = new();
    private readonly Dictionary<string, RotatableSession> sessionsByTokenHash =
        new(StringComparer.Ordinal);

    /// <summary>
    /// Adds an existing session (seed data). Sessions are created by the login flow of Team 1,
    /// so the port itself has no create operation.
    /// </summary>
    public void Add(RotatableSession session)
    {
        ArgumentNullException.ThrowIfNull(session);

        lock (gate)
        {
            if (!sessionsByTokenHash.TryAdd(session.TokenHash, session))
            {
                throw new InvalidOperationException("A session with the same token hash already exists.");
            }
        }
    }

    public ValueTask<RotatableSession?> FindAsync(
        string tokenHash,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(tokenHash);
        cancellationToken.ThrowIfCancellationRequested();

        lock (gate)
        {
            return ValueTask.FromResult(
                sessionsByTokenHash.TryGetValue(tokenHash, out var session) ? session : null);
        }
    }

    public ValueTask<SessionRotationStoreStatus> RotateAsync(
        string oldTokenHash,
        RotatableSession replacement,
        DateTimeOffset now,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(oldTokenHash);
        ArgumentNullException.ThrowIfNull(replacement);
        cancellationToken.ThrowIfCancellationRequested();

        lock (gate)
        {
            if (!sessionsByTokenHash.TryGetValue(oldTokenHash, out var current))
            {
                return ValueTask.FromResult(SessionRotationStoreStatus.NotFound);
            }

            if (current.IsRevoked)
            {
                return ValueTask.FromResult(SessionRotationStoreStatus.AlreadyRevoked);
            }

            if (current.IsExpiredAt(now))
            {
                return ValueTask.FromResult(SessionRotationStoreStatus.Expired);
            }

            if (replacement.ChainId != current.ChainId
                || replacement.AbsoluteExpiresAt != current.AbsoluteExpiresAt)
            {
                throw new ArgumentException(
                    "The replacement must keep the chain and the absolute expiry of the old session.",
                    nameof(replacement));
            }

            if (sessionsByTokenHash.ContainsKey(replacement.TokenHash))
            {
                throw new InvalidOperationException("A session with the same token hash already exists.");
            }

            sessionsByTokenHash[oldTokenHash] = current with { RevokedAt = now };
            sessionsByTokenHash.Add(replacement.TokenHash, replacement);
            return ValueTask.FromResult(SessionRotationStoreStatus.Rotated);
        }
    }

    public ValueTask<int> RevokeChainAsync(
        Guid chainId,
        DateTimeOffset now,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return ValueTask.FromResult(RevokeActive(session => session.ChainId == chainId, now));
    }

    public ValueTask<int> RevokeAllForSubjectAsync(
        string subjectId,
        DateTimeOffset now,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(subjectId);
        cancellationToken.ThrowIfCancellationRequested();
        return ValueTask.FromResult(RevokeActive(
            session => string.Equals(session.SubjectId, subjectId, StringComparison.Ordinal),
            now));
    }

    private int RevokeActive(Func<RotatableSession, bool> belongsToTarget, DateTimeOffset now)
    {
        lock (gate)
        {
            var toRevoke = sessionsByTokenHash.Values
                .Where(session => !session.IsRevoked && belongsToTarget(session))
                .ToList();

            foreach (var session in toRevoke)
            {
                sessionsByTokenHash[session.TokenHash] = session with { RevokedAt = now };
            }

            return toRevoke.Count;
        }
    }
}
