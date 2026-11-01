using System.Data.Common;
using Microsoft.EntityFrameworkCore;
using Platform.Api.Domain.Authentication;
using Platform.Identity.Domain;
using Platform.Identity.Services;

namespace Platform.Api.Services.Identity;

/// <summary>Connects login to the session store (US-03). Fails closed: a database error means no session.</summary>
public sealed class StoreSessionCreator(ISessionStore store, ILogger<StoreSessionCreator> logger) : ISessionCreator
{
    // US-14 moves this into configuration and adds the inactivity timeout.
    public static readonly TimeSpan Lifetime = TimeSpan.FromHours(8);

    public async Task<IssuedSession?> CreateAsync(Guid userId, AuthenticationLevel level, CancellationToken ct = default)
    {
        var storedLevel = ToStoredLevel(level);

        try
        {
            var (token, session) = await store.CreateAsync(userId, storedLevel, Lifetime, ct);
            return new IssuedSession(token, session.SessionHandle, session.ExpiresAt);
        }
        catch (Exception ex) when (ex is DbUpdateException or DbException)
        {
            logger.LogError(ex, "The session could not be stored.");
            return null;
        }
    }

    // Mapped by name: the two enums have different numeric values, so a cast would turn Active into MfaPending.
    private static AuthLevel ToStoredLevel(AuthenticationLevel level) => level switch
    {
        AuthenticationLevel.MfaPending => AuthLevel.MfaPending,
        AuthenticationLevel.Active => AuthLevel.Active,
        _ => throw new ArgumentOutOfRangeException(nameof(level), level, "A session needs an authentication level.")
    };
}
