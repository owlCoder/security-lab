using Platform.Api.Domain.Authentication;
using Platform.Api.Services.Identity;
using Platform.Identity.Domain;

namespace Platform.Tests.Identity;

[TestFixture]
[Category("US-06")]
public class StoreSessionManagerTests
{
    private FakeSessionStore _store = null!;
    private StoreSessionManager _manager = null!;

    [SetUp]
    public void SetUp()
    {
        _store = new FakeSessionStore();
        _manager = new StoreSessionManager(_store);
    }

    [Test]
    public async Task UnknownToken_ReturnsNull()
    {
        _store.Session = null;

        var result = await _manager.ValidateAsync("unknown-token");

        Assert.That(result, Is.Null);
    }

    [Test]
    public async Task ExpiredSession_ReturnsNull()
    {
        _store.Session = NewSession(AuthLevel.Active, expiresIn: TimeSpan.FromMinutes(-1));

        var result = await _manager.ValidateAsync("token");

        Assert.That(result, Is.Null);
    }

    [Test]
    public async Task ValidSession_ReturnsHandleUserAndActiveLevel()
    {
        var session = NewSession(AuthLevel.Active, expiresIn: TimeSpan.FromHours(1));
        _store.Session = session;

        var result = await _manager.ValidateAsync("token");

        Assert.That(result, Is.Not.Null);
        Assert.That(result!.Handle, Is.EqualTo(session.SessionHandle));
        Assert.That(result.Handle, Is.Not.EqualTo(session.Id));
        Assert.That(result.UserId, Is.EqualTo(session.UserId));
        Assert.That(result.Level, Is.EqualTo(AuthenticationLevel.Active));
    }

    [Test]
    public async Task MfaPendingSession_MapsToMfaPending()
    {
        _store.Session = NewSession(AuthLevel.MfaPending, expiresIn: TimeSpan.FromHours(1));

        var result = await _manager.ValidateAsync("token");

        Assert.That(result!.Level, Is.EqualTo(AuthenticationLevel.MfaPending));
    }

    // Opozvana sesija: čeka opoziv iz US-03 (RevokedAt se trenutno ne može postaviti).

    private static Session NewSession(AuthLevel level, TimeSpan expiresIn)
    {
        var now = DateTimeOffset.UtcNow;
        return new Session(Guid.NewGuid(), Guid.NewGuid(), new byte[32], Guid.NewGuid(),
            level, now, now + expiresIn);
    }

    private sealed class FakeSessionStore : ISessionStore
    {
        public Session? Session { get; set; }

        public Task<Session?> FindByTokenAsync(string rawToken, CancellationToken ct) =>
            Task.FromResult(Session);

        public Task<(string RawToken, Session Session)> CreateAsync(
            Guid userId, AuthLevel level, TimeSpan lifetime, CancellationToken ct) =>
            throw new NotSupportedException();
    }
}