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

        var now = clock.GetUtcNow();
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

        session.Revoke(clock.GetUtcNow());
        await db.SaveChangesAsync(ct);
        return true;
    }

    public Task<ValidateSession?> ValidateAsync(string token, CancellationToken ct = default) =>
        CheckAsync(token, recordActivity: true, ct);

    // US-15: the CSRF check asks whether the session is valid before it refuses a request. A refused request must
    // not count as activity (US-14 inactivity), so this runs the same checks without Touch.
    public Task<ValidateSession?> PeekAsync(string token, CancellationToken ct = default) =>
        CheckAsync(token, recordActivity: false, ct);

    private async Task<ValidateSession?> CheckAsync(string token, bool recordActivity, CancellationToken ct)
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

        if (recordActivity)
        {
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

        var now = clock.GetUtcNow();
        return await db.Sessions
            // IDN-07 lists the sessions the user currently has access from. An MfaPending session (password
            // accepted, second factor not yet, IDN-06) gives no access, so it is not listed.
            .Where(session =>
                session.UserId == userId &&
                session.AuthLevel == AuthLevel.Active &&
                session.RevokedAt == null &&
                session.ExpiresAt > now)
            .Select(session => new SessionInfo(
                session.SessionHandle,
                session.CreatedAt,
                session.LastActivityAt,
                session.ExpiresAt))
            .ToListAsync(ct);
    }
}
