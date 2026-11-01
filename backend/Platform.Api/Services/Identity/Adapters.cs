using System.Collections.Concurrent;
using Platform.Identity.Domain;
using Platform.Api.Domain.Authentication;
using System.Reflection.Metadata.Ecma335;

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
public sealed class NoOpSessionRevoker : ISessionManager
{
    public int RevokeAllForUser(Guid userId, string reason, CancellationToken ct = default) => 0;
    public bool RevokeSession(Guid ownerUserId, Guid sessionHandle, CancellationToken ct = default) => false;
    public IReadOnlyList<SessionInfo> ListForUser(Guid userId, CancellationToken ct = default) => Array.Empty<SessionInfo>();
    public Task<ValidateSession?> ValidateAsync(string token, CancellationToken ct = default) => Task.FromResult<ValidateSession?>(null);
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