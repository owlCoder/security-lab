using Microsoft.EntityFrameworkCore;
using Platform.Identity.Entities;
using Platform.Identity.Enums;
using Platform.Identity.Persistence;
using Platform.SessionSecurity.Contracts;

// IDN-11 TIM 09
namespace Platform.Api.Modules.SessionSecurity.Sessions;

/// <summary>
/// IDN-11: <see cref="ISessionRotationStore"/> over <c>identity.sessions</c> (Team 1), without a schema
/// change. A rotated session is an ordinary Team 1 session, so SessionCheckMiddleware rejects the old
/// token and accepts the new one (SEC-03).
/// </summary>
/// <remarks>
/// <para>
/// D4: identity.sessions has no chain, so <see cref="RotatableSession.ChainId"/> is the
/// <see cref="Session.SessionHandle"/>. A rotation replaces the token hash in the same row, so the handle,
/// <see cref="Session.CreatedAt"/> and <see cref="Session.ExpiresAt"/> stay the same: a handle the user
/// listed before a rotation still revokes the session after it (IDN-07), and the chain is stable. The old
/// hash no longer exists, so the old token is rejected everywhere. A chain is therefore exactly one row:
/// <see cref="RevokeChainAsync"/> revokes only the session with that handle, and the other sessions of the
/// subject stay valid (IDN-07). Only <see cref="RevokeAllForSubjectAsync"/> revokes all of them (IDN-03).
/// </para>
/// <para>
/// D5: "pending activation" is logical. <see cref="RotateAsync"/> stores the new hash in the active
/// Team 1 session, but its token leaves the server only after the rotation was audited; when the audit
/// fails, the activation fails or the request is cancelled,
/// <see cref="Platform.SessionSecurity.Application.SessionRotationService"/> revokes the chain, so this
/// session ends and the new token is never valid (AUD-10).
/// <see cref="ActivateAsync"/> therefore changes nothing and only checks that the session is still active.
/// </para>
/// <para>
/// D6, D13: the expiry is never changed. A rotation locks the account (FOR SHARE) before the session
/// (FOR UPDATE), in the same order as the Team 1 status change, so a concurrent block cannot deadlock with
/// it and no session outlives a block (IDN-03). A rotation never writes RevokedAt, the concurrency token
/// of a Team 1 revocation, so a concurrent IDN-07 revocation waits for the row lock and then succeeds.
/// </para>
/// <para>
/// A reused old token is reported as not found instead of revoked. The client gets the same generic
/// answer for both; only the audit reason code differs.
/// </para>
/// </remarks>
internal sealed class IdentitySessionRotationStore(PlatformIdentityDbContext db) : ISessionRotationStore
{
    public async ValueTask<RotatableSession?> FindAsync(
        string tokenHash,
        CancellationToken cancellationToken = default)
    {
        if (!SessionTokenHashConverter.TryToBytes(tokenHash, out var hash))
        {
            return null;
        }

        var session = await db.Sessions
            .AsNoTracking()
            .SingleOrDefaultAsync(candidate => candidate.TokenHash == hash, cancellationToken);
        return session is null ? null : ToRotatable(session);
    }

    public async ValueTask<SessionRotationStoreStatus> RotateAsync(
        string oldTokenHash,
        RotatableSession replacement,
        DateTimeOffset now,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(replacement);
        if (!SessionTokenHashConverter.TryToBytes(oldTokenHash, out var oldHash))
        {
            return SessionRotationStoreStatus.NotFound;
        }

        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);

        // D13: the session key is read without a lock, so the account can be locked before the session.
        var key = await FindKeyAsync(oldHash, cancellationToken);
        if (key is null)
        {
            return SessionRotationStoreStatus.NotFound;
        }

        if (!await LockActiveAccountAsync(key.UserId, cancellationToken))
        {
            return SessionRotationStoreStatus.AlreadyRevoked;
        }

        var current = await LockSessionAsync(key.Id, cancellationToken);
        var status = CheckLockedSession(current, key.UserId, oldHash, now);
        if (status is not null)
        {
            return status.Value;
        }

        // Not null: CheckLockedSession returns a status for a missing session.
        if (!await ReplaceTokenAsync(current!, oldHash, replacement, now, cancellationToken))
        {
            return SessionRotationStoreStatus.AlreadyRevoked;
        }

        await transaction.CommitAsync(cancellationToken);
        return SessionRotationStoreStatus.Rotated;
    }

    public async ValueTask<bool> ActivateAsync(
        string tokenHash,
        DateTimeOffset now,
        CancellationToken cancellationToken = default)
    {
        if (!SessionTokenHashConverter.TryToBytes(tokenHash, out var hash))
        {
            return false;
        }

        var session = await db.Sessions
            .AsNoTracking()
            .SingleOrDefaultAsync(candidate => candidate.TokenHash == hash, cancellationToken);

        // D5: the Team 1 activity rule decides, so a later change of it applies here as well.
        return session is not null && session.IsActiveAt(now);
    }

    public async ValueTask<int> RevokeChainAsync(
        Guid chainId,
        DateTimeOffset now,
        CancellationToken cancellationToken = default)
    {
        // D4: the chain is the one session with this handle; other sessions of the subject are not touched.
        return await db.Sessions
            .Where(session => session.SessionHandle == chainId && session.RevokedAt == null)
            .ExecuteUpdateAsync(set => set.SetProperty(session => session.RevokedAt, (DateTimeOffset?)now), cancellationToken);
    }

    public async ValueTask<int> RevokeAllForSubjectAsync(
        string subjectId,
        DateTimeOffset now,
        CancellationToken cancellationToken = default)
    {
        // D1: the subject is the user ID in the "D" format; anything else matches no session.
        return Guid.TryParseExact(subjectId, "D", out var userId)
            ? await RevokeAllAsync(userId, now, cancellationToken)
            : 0;
    }

    private Task<SessionKey?> FindKeyAsync(byte[] tokenHash, CancellationToken cancellationToken) =>
        db.Sessions
            .AsNoTracking()
            .Where(session => session.TokenHash == tokenHash)
            .Select(session => new SessionKey(session.Id, session.UserId))
            .SingleOrDefaultAsync(cancellationToken);

    // FOR SHARE pairs with the FOR UPDATE of the Team 1 status change, the same lock as
    // SessionStore.CreateForActiveAccountAsync (IDN-03).
    private async Task<bool> LockActiveAccountAsync(Guid userId, CancellationToken cancellationToken)
    {
        var account = await db.Accounts
            .FromSqlInterpolated($"""
                SELECT * FROM identity.accounts WHERE id = {userId} FOR SHARE
                """)
            .AsNoTracking()
            .SingleOrDefaultAsync(cancellationToken);

        return account is not null && account.Status == AccountStatus.Active;
    }

    // Not tracked: the request's context may already track this session (SessionManager.ValidateAsync)
    // with values from before the lock, and the checks below need the locked row.
    private Task<Session?> LockSessionAsync(Guid sessionId, CancellationToken cancellationToken) =>
        db.Sessions
            .FromSqlInterpolated($"""
                SELECT * FROM identity.sessions WHERE id = {sessionId} FOR UPDATE
                """)
            .AsNoTracking()
            .SingleOrDefaultAsync(cancellationToken);

    // A concurrent rotation that committed first is seen here as a changed token hash.
    private static SessionRotationStoreStatus? CheckLockedSession(
        Session? current,
        Guid userId,
        byte[] oldHash,
        DateTimeOffset now)
    {
        if (current is null || current.UserId != userId)
        {
            return SessionRotationStoreStatus.NotFound;
        }

        if (!current.TokenHash.AsSpan().SequenceEqual(oldHash) || current.RevokedAt is not null)
        {
            return SessionRotationStoreStatus.AlreadyRevoked;
        }

        return current.ExpiresAt <= now ? SessionRotationStoreStatus.Expired : null;
    }

    // D4: the same row gets the new hash, so Id, SessionHandle, CreatedAt and ExpiresAt (D6) stay. The new
    // token starts its activity now, as a Team 1 login does. RevokedAt is not written (see the remarks).
    private async Task<bool> ReplaceTokenAsync(
        Session current,
        byte[] oldHash,
        RotatableSession replacement,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        var (replacementHash, level) = ValidateReplacement(current, replacement);

        // The row is locked by this transaction; the filter repeats the checks above as a second guard.
        var updated = await db.Sessions
            .Where(session => session.Id == current.Id && session.TokenHash == oldHash && session.RevokedAt == null)
            .ExecuteUpdateAsync(set => set
                .SetProperty(session => session.TokenHash, replacementHash)
                .SetProperty(session => session.AuthLevel, level)
                .SetProperty(session => session.LastActivityAt, now), cancellationToken);

        return updated == 1;
    }

    // Programming errors of the caller, checked before anything is written. The messages carry no values.
    private static (byte[] Hash, AuthLevel Level) ValidateReplacement(Session current, RotatableSession replacement)
    {
        if (replacement.ChainId != current.SessionHandle || replacement.AbsoluteExpiresAt != current.ExpiresAt)
        {
            throw new InvalidOperationException(
                "The replacement must keep the chain and the absolute expiry of the old session.");
        }

        if (!replacement.IsPendingActivation || replacement.IsRevoked)
        {
            throw new InvalidOperationException(
                "The replacement must be stored as pending activation and not revoked.");
        }

        if (!SessionTokenHashConverter.TryToBytes(replacement.TokenHash, out var hash))
        {
            throw new InvalidOperationException("The replacement must carry a SHA-256 token hash.");
        }

        return (hash, SessionPrivilegeLevelMapper.ToIdentity(replacement.PrivilegeLevel));
    }

    private Task<int> RevokeAllAsync(Guid userId, DateTimeOffset now, CancellationToken cancellationToken) =>
        db.Sessions
            .Where(session => session.UserId == userId && session.RevokedAt == null)
            .ExecuteUpdateAsync(set => set.SetProperty(session => session.RevokedAt, (DateTimeOffset?)now), cancellationToken);

    private static RotatableSession ToRotatable(Session session) => new(
        session.SessionHandle,
        session.UserId.ToString("D"),
        SessionTokenHashConverter.ToHex(session.TokenHash),
        SessionPrivilegeLevelMapper.ToCore(session.AuthLevel),
        session.CreatedAt,
        session.ExpiresAt,
        session.RevokedAt,
        IsPendingActivation: false);

    // Not a secret: the row ID and the owner, read before any lock (D13).
    private sealed record SessionKey(Guid Id, Guid UserId);
}
