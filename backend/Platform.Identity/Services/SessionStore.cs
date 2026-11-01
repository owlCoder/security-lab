
using Microsoft.EntityFrameworkCore;
using Platform.Identity.Data;
using Platform.Identity.Domain;
namespace Platform.Identity.Services;

internal sealed class SessionStore(PlatformIdentityDbContext db, TimeProvider time) : ISessionStore
{
    public async Task<(string RawToken, Session Session)> CreateAsync(
        Guid userId, AuthLevel level, TimeSpan lifetime, CancellationToken ct)
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

        db.Sessions.Add(session);
        await db.SaveChangesAsync(ct);

        return (rawToken, session);
    }

    public async Task<Session?> FindByTokenAsync(string rawToken, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(rawToken))
            return null;

        var hash = SessionTokenGenerator.Hash(rawToken);
        return await db.Sessions.FirstOrDefaultAsync(s => s.TokenHash == hash, ct);
    }

}
