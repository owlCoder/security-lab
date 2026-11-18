using Platform.Api.Interfaces.Identity;
using Platform.Api.Models.Identity;
using Platform.Api.Services.Identity;
using Platform.Identity.Entities;
using Platform.Identity.Enums;
using Platform.Identity.Interfaces;
using Platform.Audit.Contracts;
using Microsoft.Extensions.Logging.Abstractions;

namespace Platform.Identity.Tests.Services;

[TestFixture]
public class LogoutTests
{
    private const string Token = "opaque-session-token-value";

    private FakeStore _store = null!;
    private RecordingAudit _audit = null!;
    private LogoutService _logout = null!;

    [SetUp]
    public void SetUp()
    {
        _store = new FakeStore();
        _audit = new RecordingAudit();
        _logout = new LogoutService(
            _store,
            new IdentityAuditProducer(_audit, NullLogger<IdentityAuditProducer>.Instance));
    }

    [Test]
    public async Task ValidSession_IsRevoked_AndAudited()
    {
        var session = _store.Add(Token, Guid.NewGuid());

        await _logout.ExecuteAsync(Token, "corr-1");

        Assert.That(session.RevokedAt, Is.Not.Null);
        var e = _audit.Events.Single();
        Assert.That(e.Draft.EventType, Is.EqualTo(AuditEventTypes.AuthLogout));
        Assert.That(e.Context.Actor.Id, Is.EqualTo(session.UserId));
        Assert.That(e.Draft.Resource, Is.Null);
        Assert.That(e.Draft.Outcome, Is.EqualTo(AuditOutcome.Success));
        Assert.That(e.Context.CorrelationId, Is.EqualTo("corr-1"));
    }

    [Test]
    public async Task MfaPendingSession_IsRevoked()
    {
        var session = _store.Add(Token, Guid.NewGuid(), AuthLevel.MfaPending);

        await _logout.ExecuteAsync(Token, "corr-2");

        Assert.That(session.RevokedAt, Is.Not.Null);
    }

    [Test]
    public async Task OtherSessionOfTheSameUser_StaysValid()
    {
        var userId = Guid.NewGuid();
        _store.Add(Token, userId);
        var other = _store.Add("another-token", userId);

        await _logout.ExecuteAsync(Token, "corr-3");

        Assert.That(other.RevokedAt, Is.Null);
    }

    [TestCase(null)]
    [TestCase("")]
    [TestCase("unknown-token")]
    public async Task NoSessionToRevoke_DoesNothing_AndWritesNoAudit(string? token)
    {
        await _logout.ExecuteAsync(token, "corr-4");

        Assert.That(_audit.Events, Is.Empty);
    }

    [Test]
    public async Task SecondLogout_WritesNoSecondAudit()
    {
        _store.Add(Token, Guid.NewGuid());

        await _logout.ExecuteAsync(Token, "corr-5");
        await _logout.ExecuteAsync(Token, "corr-6");

        Assert.That(_audit.Events, Has.Count.EqualTo(1));
    }

    [Test]
    public async Task AuditEvent_NeverContainsTheToken()
    {
        _store.Add(Token, Guid.NewGuid());

        await _logout.ExecuteAsync(Token, "corr-7");

        var e = _audit.Events.Single();
        Assert.That(string.Join('|', e.Draft.EventType, e.Draft.Outcome, e.Context.CorrelationId),
            Does.Not.Contain(Token));
    }

    [Test]
    public async Task AuditUnavailable_DoesNotRestoreRevokedSession()
    {
        var session = _store.Add(Token, Guid.NewGuid());
        _audit.Result = AuditWriteResult.Unavailable();

        await _logout.ExecuteAsync(Token, "corr-audit-down");

        Assert.That(session.RevokedAt, Is.Not.Null);
    }

    private sealed class FakeStore : ISessionStore
    {
        private readonly Dictionary<string, Session> _sessions = [];

        public Session Add(string token, Guid userId, AuthLevel level = AuthLevel.Active)
        {
            var now = DateTimeOffset.UtcNow;
            var session = new Session(Guid.NewGuid(), userId, new byte[32], Guid.NewGuid(), level, now,
                now.AddHours(8));
            _sessions[token] = session;
            return session;
        }

        public Task<Session?> RevokeByTokenAsync(string rawToken, CancellationToken ct)
        {
            if (!_sessions.TryGetValue(rawToken, out var session) || session.RevokedAt is not null)
                return Task.FromResult<Session?>(null);

            session.Revoke(DateTimeOffset.UtcNow);
            return Task.FromResult<Session?>(session);
        }

        public Task<(string RawToken, Session Session)> CreateAsync(
            Guid userId, AuthLevel level, TimeSpan lifetime, CancellationToken ct) => throw new NotSupportedException();

        public Task<(string RawToken, Session Session)?> CreateForActiveAccountAsync(
            Guid userId, AuthLevel level, TimeSpan lifetime, CancellationToken ct) => throw new NotSupportedException();

        public Task<Session?> FindByTokenAsync(string rawToken, CancellationToken ct) =>
            throw new NotSupportedException();

        public Task TouchAsync(Session session, DateTimeOffset now, CancellationToken ct) =>
            throw new NotSupportedException();
    }

    private sealed record RecordedAudit(AuditEventDraft Draft, AuditContext Context);

    private sealed class RecordingAudit : ITrustedAuditWriter
    {
        public List<RecordedAudit> Events { get; } = [];
        public AuditWriteResult Result { get; set; } = AuditWriteResult.Recorded(Guid.NewGuid());

        public ValueTask<AuditWriteResult> WriteAsync(
            AuditEventDraft draft,
            AuditContext context,
            CancellationToken cancellationToken = default)
        {
            Events.Add(new RecordedAudit(draft, context));
            return ValueTask.FromResult(Result);
        }
    }
}
