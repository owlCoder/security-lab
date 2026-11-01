using System.Collections.Concurrent;
using Platform.Identity.Domain;
using Platform.Api.Domain.Authentication;

namespace Platform.Api.Services.Identity;

// TODO(IDN-03 / ADM-02): PRIVREMENO. Podaci su samo u memoriji i nestaju kad se aplikacija ugasi,
// pa kriterijum "sistem čuva novi status u bazi" nije do kraja ispunjen.
// Trajno čuvanje (PostgreSQL + migracija) radi . Kad bude gotovo, dovoljno je
// da se napravi nova implementacija IAccountRepository i zameni registracija u Program.cs.
// Use-case (ChangeAccountStatus) i testovi se ne menjaju.
public sealed class InMemoryAccountRepository : IAccountRepository
{
    private readonly ConcurrentDictionary<Guid, UserAccount> _items = new();
    public UserAccount? Find(Guid id) => _items.GetValueOrDefault(id);
    public void Save(UserAccount account) => _items[account.Id] = account;
}

/// Stub dok IDN-02 ne napravi pravi opoziv sesija.
/// Proverava sesiju u bazi (US-06). Opoziv jos ne postoji.
public sealed class StoreSessionManager : ISessionManager
{
    public int RevokeAllForUser(Guid userId, string reason, CancellationToken ct = default) => 0;
    public bool RevokeSession(Guid ownerUserId, Guid sessionHandle, CancellationToken ct = default) => false;
    public IReadOnlyList<SessionInfo> ListForUser(Guid userId, CancellationToken ct = default) => Array.Empty<SessionInfo>();
    private readonly ISessionStore _sessionStore = null!;
    public StoreSessionManager(ISessionStore sessionStore)
    {
        _sessionStore = sessionStore;
    }

    private static AuthenticationLevel MapLevel(AuthLevel level) => level switch
    {
        AuthLevel.Active => AuthenticationLevel.Active,
        AuthLevel.MfaPending => AuthenticationLevel.MfaPending,
        _ => AuthenticationLevel.None
    };

    public async Task<ValidateSession?> ValidateAsync(string token, CancellationToken ct = default)
    {
        var session = await _sessionStore.FindByTokenAsync(token, ct);

        if (session == null)
        {
            return null;
        }

        var now = DateTimeOffset.UtcNow;

        if (session.RevokedAt != null || session.ExpiresAt <= now)
        {
            return null;
        }

        await _sessionStore.TouchAsync(session, now, ct);

        ValidateSession validate = new ValidateSession(session.SessionHandle, session.UserId, MapLevel(session.AuthLevel));

        return validate;
    }
}

public sealed class LoggerAuditWriter(ILogger<LoggerAuditWriter> logger) : IAuditWriter
{
    public void Write(AuditEvent e) =>
        logger.LogInformation(
            "AUDIT {Time} actor={Actor} action={Action} target={Target} outcome={Outcome} corr={Corr} detail={Detail}",
            e.Time, e.ActorId, e.Action, e.Target, e.Outcome, e.CorrelationId, e.Detail);
}

public interface IAdminDirectory
{
    bool IsAdministrator(Guid userId);
}

/// Samo administrator sme da menja status. Sve ostalo je zabranjeno.
public sealed class AdminOnlyAccessDecision(IAdminDirectory admins) : IAccessDecision
{
    public bool CanChangeAccountStatus(CallerIdentity caller) =>
        caller.IsAuthenticated && admins.IsAdministrator(caller.UserId ?? Guid.Empty);
}

/// DEV STUB do IDN-01/IDN-02: pozivalac se cita iz headera X-Stub-User.
/// Registruje se SAMO u Development okruzenju.
public sealed class StubIdentityContext(IHttpContextAccessor http) : IIdentityContext
{
    public CallerIdentity? Current
    {
        get
        {
            var raw = http.HttpContext?.Request.Headers["X-Stub-User"].FirstOrDefault();
            return Guid.TryParse(raw, out var id) ? new CallerIdentity(true, id, AuthenticationLevel.None, new Guid()) : null;
        }
    }
}

/// Izvan Developmenta nema pozivaoca, pa je svaki zahtev 401.
public sealed class AnonymousIdentityContext : IIdentityContext
{
    public CallerIdentity? Current => null;
}

/// DEV STUB: id-jevi administratora iz konfiguracije (Stub:AdminIds, odvojeni zarezom).
public sealed class ConfigAdminDirectory(IConfiguration config) : IAdminDirectory
{
    private readonly HashSet<Guid> _ids = (config["Stub:AdminIds"] ?? "")
        .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
        .Select(s => Guid.TryParse(s, out var g) ? g : Guid.Empty)
        .Where(g => g != Guid.Empty)
        .ToHashSet();

    public bool IsAdministrator(Guid userId) => _ids.Contains(userId);
}

/// US-06: pozivalac se cita iz sesije koju je SessionCheckMiddleware vec proverio.
public sealed class SessionIdentityContext : IIdentityContext
{
    private readonly IHttpContextAccessor _httpContextAccessor;

    public SessionIdentityContext(IHttpContextAccessor httpContextAccessor)
    {
        _httpContextAccessor = httpContextAccessor;
    }

    public CallerIdentity? Current
    {
        get
        {
            var session = _httpContextAccessor.HttpContext?.Items[SessionCheckMiddleware.SessionItemKey] as ValidateSession;

            if (session == null)
                return null;

            return new CallerIdentity(
                session.Level == AuthenticationLevel.Active,
                session.UserId,
                session.Level,
                session.Handle);
        }
    }
}