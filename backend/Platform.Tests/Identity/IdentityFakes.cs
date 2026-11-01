using System.Collections.Concurrent;
using Microsoft.Extensions.DependencyInjection;
using Platform.Api.Domain.Authentication;

namespace Platform.Api.Services.Identity;

/// <summary>Settable fake for tests and for teams waiting on the real implementation.</summary>
public sealed class FakeIdentityContext : IIdentityContext
{
    public Guid? UserId { get; set; }
    public AuthenticationLevel Level { get; set; } = AuthenticationLevel.None;
    public Guid? CurrentSessionHandle { get; set; }

    // Rule from the contract: MfaPending is not authenticated.
    public bool IsAuthenticated => UserId.HasValue && Level == AuthenticationLevel.Active;

    public CallerIdentity? Current => new(IsAuthenticated, UserId, Level, CurrentSessionHandle);
}

/// <summary>In-memory fake. Replaced by the database-backed store in US-03.</summary>
public sealed class InMemorySessionManagement : ISessionManager
{
    private sealed class Entry
    {
        public Guid UserId;
        public DateTimeOffset CreatedAt;
        public DateTimeOffset LastActivityAt;
        public DateTimeOffset ExpiresAt;
        public bool Revoked;
    }

    private readonly ConcurrentDictionary<Guid, Entry> _sessions = new();

    /// <summary>Test helper: creates a session and returns its public handle.</summary>
    public Guid Seed(Guid userId, TimeSpan? lifetime = null)
    {
        var now = DateTimeOffset.UtcNow;
        var handle = Guid.NewGuid();
        _sessions[handle] = new Entry
        {
            UserId = userId,
            CreatedAt = now,
            LastActivityAt = now,
            ExpiresAt = now + (lifetime ?? TimeSpan.FromHours(8))
        };
        return handle;
    }
    public bool IsSessionActive(Guid userId, Guid sessionHandle, CancellationToken ct = default)
    { 
        if (userId == Guid.Empty) throw new ArgumentException("ownerUserId is required", nameof(userId));
        if (sessionHandle == Guid.Empty) throw new ArgumentException("sessionHandle is required", nameof(sessionHandle));

        return _sessions.TryGetValue(sessionHandle, out var e) && IsLive(e);
    }

    /// <summary>Test helper: is the session still valid?</summary>
    public bool IsActive(Guid handle) =>
        _sessions.TryGetValue(handle, out var e) && IsLive(e);

    private static bool IsLive(Entry e) => !e.Revoked && e.ExpiresAt > DateTimeOffset.UtcNow;

    public int RevokeAllForUser(Guid userId, string reason, CancellationToken ct = default)
    {
        if (userId == Guid.Empty) throw new ArgumentException("userId is required", nameof(userId));

        var count = 0;
        foreach (var e in _sessions.Values)
        {
            if (e.UserId != userId) continue;
            lock (e)
            {
                if (e.Revoked) continue;
                e.Revoked = true;
                count++;
            }
        }
        return count;
    }

    public bool RevokeSession(Guid ownerUserId, Guid sessionHandle, CancellationToken ct = default)
    {
        if (ownerUserId == Guid.Empty) throw new ArgumentException("ownerUserId is required", nameof(ownerUserId));
        if (sessionHandle == Guid.Empty) throw new ArgumentException("sessionHandle is required", nameof(sessionHandle));

        if (!_sessions.TryGetValue(sessionHandle, out var e) || e.UserId != ownerUserId)
            return false; // missing and foreign look the same

        lock (e) { e.Revoked = true; }
        return true;
    }

    public IReadOnlyList<SessionInfo> ListForUser(Guid userId, CancellationToken ct = default)
    {
        if (userId == Guid.Empty) throw new ArgumentException("userId is required", nameof(userId));

        IReadOnlyList<SessionInfo> list = _sessions
            .Where(kv => kv.Value.UserId == userId && IsLive(kv.Value))
            .Select(kv => new SessionInfo(kv.Key, kv.Value.CreatedAt, kv.Value.LastActivityAt, kv.Value.ExpiresAt, kv.Value.Revoked))
            .ToList();
        return list;
    }
}

/// <summary>Single entry point for Program.cs: builder.Services.AddSessionsAndMfaModule();</summary>
public static class SessionsAndMfaModuleRegistration
{
    public static IServiceCollection AddSessionsAndMfaModule(this IServiceCollection services)
    {
        services.AddSingleton<ISessionManager, InMemorySessionManagement>();
        // IIdentityContext is NOT registered here yet: IDN-03 registers its dev stub in Program.cs.
        // The real implementation (US-06) will be registered here and the stub removed.
        return services;
    }
}
