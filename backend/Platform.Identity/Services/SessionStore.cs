
using Microsoft.EntityFrameworkCore;
using Platform.Identity.Entities;
using Platform.Identity.Enums;
using Platform.Identity.Interfaces;
using Platform.Identity.Persistence;
namespace Platform.Identity.Services;

internal sealed class SessionStore(PlatformIdentityDbContext db, TimeProvider time) : ISessionStore
{
    public async Task<(string RawToken, Session Session)> CreateAsync(
        Guid userId, AuthLevel level, TimeSpan lifetime, CancellationToken ct)
    {
        var created = NewSession(userId, level, lifetime);

        db.Sessions.Add(created.Session);
        await db.SaveChangesAsync(ct);

        return created;
    }

    public async Task<(string RawToken, Session Session)?> CreateForActiveAccountAsync(
        Guid userId, AuthLevel level, TimeSpan lifetime, CancellationToken ct)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(ct);

        // FOR SHARE pairs with the FOR UPDATE of ChangeAccountStatus (IDN-03): a status change that is already
        // running makes this wait and then read the new status; one that starts later waits for this commit
        // and revokes the session we are about to store. Either way no session outlives a block (SEC-03).
        var account = await db.Accounts
            .FromSqlInterpolated($"""
                SELECT * FROM identity.accounts WHERE id = {userId} FOR SHARE
                """)
            .AsNoTracking()
            .SingleOrDefaultAsync(ct);

        if (account is null || account.Status != AccountStatus.Active)
            return null;

        var created = NewSession(userId, level, lifetime);
        db.Sessions.Add(created.Session);
        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);

        return created;
    }

    private (string RawToken, Session Session) NewSession(Guid userId, AuthLevel level, TimeSpan lifetime)
    {
        var rawToken = SessionTokenGenerator.NewToken();
        var now = time.GetUtcNow();

        var session = new Session(
            id: Guid.NewGuid(),
            userId: userId,
            tokenHash: SessionTokenGenerator.Hash(rawToken),
            sessionHandle: Guid.NewGuid(),
            authLevel: level,
            createdAt: now,
            expiresAt: now.Add(lifetime));

        return (rawToken, session);
    }

    public async Task<Session?> FindByTokenAsync(string rawToken, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(rawToken))
            return null;

        var hash = SessionTokenGenerator.Hash(rawToken);
        return await db.Sessions.FirstOrDefaultAsync(s => s.TokenHash == hash, ct);
    }

    public async Task TouchAsync(Session session, DateTimeOffset now, CancellationToken ct)
    {
        session.Touch(now);
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateConcurrencyException)
        {
            db.Entry(session).State = EntityState.Detached;
        }
    }

    public async Task<Session?> RevokeByTokenAsync(string rawToken, CancellationToken ct)
    {
        var session = await FindByTokenAsync(rawToken, ct);
        var now = time.GetUtcNow();
        if (session is null || session.RevokedAt is not null || session.ExpiresAt <= now)
            return null;

        session.Revoke(now);
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateConcurrencyException)
        {
            // RevokedAt is a concurrency token: another request revoked this session first.
            db.Entry(session).State = EntityState.Detached;
            return null;
        }

        return session;
    }
}
