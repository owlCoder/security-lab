namespace Platform.Api.Services.Identity;
using System.Data.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Platform.Api.Enums.Identity;
using Platform.Api.Interfaces.Identity;
using Platform.Api.Models.Identity;
using Platform.Identity.Enums;
using Platform.Identity.Interfaces;
using Platform.Api.Modules.Identity.SessionLimiting;

/// <summary>Connects login to the session store (US-03). Fails closed: a database error means no session.</summary>
public sealed class StoreSessionCreator(ISessionStore store,IOptions<SessionOptions> sessionOptions,ILogger<StoreSessionCreator> logger) : ISessionCreator
{
    private readonly SessionOptions options = sessionOptions.Value;
    // US-14 moves this into configuration and adds the inactivity timeout.
    




    public async Task<IssuedSession?> CreateAsync(Guid userId, AuthenticationLevel level, CancellationToken ct = default)
    {
        var storedLevel = ToStoredLevel(level);

        try
        {
            
            var Lifetime = TimeSpan.FromMinutes(options.AbsoluteLifetimeMinutes);
            // Re-checks the status under a row lock: login read it earlier and it may have changed since (IDN-03).
            var created = await store.CreateForActiveAccountAsync(userId, storedLevel, Lifetime, ct);
            if (created is null) throw new AccountNotActiveException(userId);

            var (token, session) = created.Value;
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
