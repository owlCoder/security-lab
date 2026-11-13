using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Platform.Api.Enums.Identity;
using Platform.Api.Interfaces.Identity;
using Platform.Api.Models.Identity;
using Platform.Identity.Enums;
using Platform.Identity.Persistence;

namespace Platform.Api.Services.Identity;

public sealed class SessionManager(PlatformIdentityDbContext db, TimeProvider? timeProvider = null) : ISessionManager
{
    private readonly TimeProvider clock = timeProvider ?? TimeProvider.System;

    public async Task<int> RevokeAllForUser(Guid userId, string reason, CancellationToken ct = default)
    {
        if (userId == Guid.Empty) throw new ArgumentException("userId is required", nameof(userId));

        var sessions = await db.Sessions
            .Where(session => session.UserId == userId && session.RevokedAt == null)
            .ToListAsync(ct);

        if (sessions.Count == 0) return 0;

        var now = DateTimeOffset.UtcNow;
        foreach (var session in sessions)
            session.Revoke(now);

        await db.SaveChangesAsync(ct);
        return sessions.Count;
    }

    public async Task<bool> RevokeSession(Guid ownerUserId, Guid sessionHandle, CancellationToken ct = default)
    {
        if (ownerUserId == Guid.Empty) throw new ArgumentException("ownerUserId is required", nameof(ownerUserId));
        if (sessionHandle == Guid.Empty) throw new ArgumentException("sessionHandle is required", nameof(sessionHandle));

        var session = await db.Sessions.FirstOrDefaultAsync(
            candidate => candidate.SessionHandle == sessionHandle && candidate.UserId == ownerUserId,
            ct);

        if (session is null) return false;

        session.Revoke(DateTimeOffset.UtcNow);
        await db.SaveChangesAsync(ct);
        return true;
    }

    public async Task<ValidateSession?> ValidateAsync(string token, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(token)) return null;

        var tokenHash = SHA256.HashData(Encoding.UTF8.GetBytes(token));
        var session = await db.Sessions.FirstOrDefaultAsync(
            candidate => candidate.TokenHash == tokenHash,
            ct);

        var now = clock.GetUtcNow();
        if (session is null || !session.IsActiveAt(now))
            return null;

        // SEC-03 / IDN-03: a blocked or archived account is refused on its very next request, even if a session
        // was created after the revocation (a login that raced with the status change).
        var status = await db.Accounts
            .Where(account => account.Id == session.UserId)
            .Select(account => (AccountStatus?)account.Status)
            .SingleOrDefaultAsync(ct);
        if (status != AccountStatus.Active) return null;

        session.Touch(now);
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateConcurrencyException)
        {
            db.Entry(session).State = EntityState.Detached;
            return null;
        }

        var level = session.AuthLevel switch
        {
            AuthLevel.Active => AuthenticationLevel.Active,
            AuthLevel.MfaPending => AuthenticationLevel.MfaPending,
            _ => AuthenticationLevel.None
        };

        return new ValidateSession(session.SessionHandle, session.UserId, level);
    }

    public async Task<IReadOnlyList<SessionInfo>> ListForUser(Guid userId, CancellationToken ct = default)
    {
        if (userId == Guid.Empty) throw new ArgumentException("userId is required", nameof(userId));

        var now = DateTimeOffset.UtcNow;
        return await db.Sessions
            .Where(session =>
                session.UserId == userId && 
                session.RevokedAt == null &&
                    session.ExpiresAt > now)     // TODO: Once the MFA/session state model is finalized (IDN-06 / TYP-05),
                                                 // consider whether only fully authenticated sessions (AuthLevel.Active)
                                                 // should be included in this list
            .Select(session => new SessionInfo(
                session.SessionHandle,
                session.CreatedAt,
                session.LastActivityAt,
                session.ExpiresAt))
            .ToListAsync(ct);
    }
}
