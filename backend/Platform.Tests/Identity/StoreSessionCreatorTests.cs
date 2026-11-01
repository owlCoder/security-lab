using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Platform.Api.Domain.Authentication;
using Platform.Api.Services.Identity;
using Platform.Identity.Domain;
using Platform.Identity.Services;

namespace Platform.Tests.Identity;

[TestFixture]
[Category("US-04")]
public class StoreSessionCreatorTests
{
    private const string Token = "opaque-session-token-value";

    private FakeStore _store = null!;
    private StoreSessionCreator _creator = null!;

    [SetUp]
    public void SetUp()
    {
        _store = new FakeStore();
        _creator = new StoreSessionCreator(_store, NullLogger<StoreSessionCreator>.Instance);
    }

    [Test]
    public async Task Create_ReturnsTokenHandleAndExpiryFromTheStore()
    {
        var userId = Guid.NewGuid();

        var issued = await _creator.CreateAsync(userId, AuthenticationLevel.Active);

        Assert.That(issued!.Token, Is.EqualTo(Token));
        Assert.That(issued.Handle, Is.EqualTo(_store.Created!.SessionHandle));
        Assert.That(issued.ExpiresAt, Is.EqualTo(_store.Created.ExpiresAt));
        Assert.That(_store.Created.UserId, Is.EqualTo(userId));
        Assert.That(_store.LastLifetime, Is.EqualTo(StoreSessionCreator.Lifetime));
    }

    [TestCase(AuthenticationLevel.Active, AuthLevel.Active)]
    [TestCase(AuthenticationLevel.MfaPending, AuthLevel.MfaPending)]
    public async Task Create_KeepsTheAuthenticationLevel(AuthenticationLevel level, AuthLevel expected)
    {
        await _creator.CreateAsync(Guid.NewGuid(), level);

        Assert.That(_store.Created!.AuthLevel, Is.EqualTo(expected));
    }

    [Test]
    public void Create_WithoutALevel_IsRefused()
    {
        Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => _creator.CreateAsync(Guid.NewGuid(), AuthenticationLevel.None));
    }

    [Test]
    public async Task Create_DatabaseFailure_ReturnsNull()
    {
        _store.Failure = new DbUpdateException("database is down");

        Assert.That(await _creator.CreateAsync(Guid.NewGuid(), AuthenticationLevel.Active), Is.Null);
    }

    private sealed class FakeStore : ISessionStore
    {
        public Exception? Failure { get; set; }
        public Session? Created { get; private set; }
        public TimeSpan? LastLifetime { get; private set; }

        public Task<(string RawToken, Session Session)> CreateAsync(
            Guid userId, AuthLevel level, TimeSpan lifetime, CancellationToken ct)
        {
            if (Failure is not null) throw Failure;

            var now = DateTimeOffset.UtcNow;
            LastLifetime = lifetime;
            Created = new Session(Guid.NewGuid(), userId, new byte[32], Guid.NewGuid(), level, now, now + lifetime);
            return Task.FromResult((Token, Created));
        }

        public Task<Session?> FindByTokenAsync(string rawToken, CancellationToken ct) => throw new NotSupportedException();
        public Task<Session?> RevokeByTokenAsync(string rawToken, CancellationToken ct) => throw new NotSupportedException();
        public Task TouchAsync(Session session, DateTimeOffset now, CancellationToken ct) => throw new NotSupportedException();
    }
}
