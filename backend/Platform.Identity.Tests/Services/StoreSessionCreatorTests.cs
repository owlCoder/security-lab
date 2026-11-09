using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Platform.Api.Enums.Identity;
using Platform.Api.Interfaces.Identity;
using Platform.Api.Services.Identity;
using Platform.Identity.Entities;
using Platform.Identity.Enums;
using Platform.Identity.Interfaces;

namespace Platform.Identity.Tests.Services;

[TestFixture]
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
    public async Task Create_WithoutALevel_IsRefused()
    {
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => _creator.CreateAsync(Guid.NewGuid(), AuthenticationLevel.None));
    }

    [Test]
    public async Task Create_DatabaseFailure_ReturnsNull()
    {
        _store.Failure = new DbUpdateException("database is down");

        Assert.That(await _creator.CreateAsync(Guid.NewGuid(), AuthenticationLevel.Active), Is.Null);
    }

    [Test]
    public async Task Create_AccountNoLongerActive_Throws_AndStoresNoSession()
    {
        _store.AccountIsActive = false;

        await Assert.ThrowsAsync<AccountNotActiveException>(() => _creator.CreateAsync(Guid.NewGuid(), AuthenticationLevel.Active));
        Assert.That(_store.Created, Is.Null);
    }

    private sealed class FakeStore : ISessionStore
    {
        public Exception? Failure { get; set; }
        public bool AccountIsActive { get; set; } = true;
        public Session? Created { get; private set; }
        public TimeSpan? LastLifetime { get; private set; }

        public Task<(string RawToken, Session Session)> CreateAsync(
            Guid userId, AuthLevel level, TimeSpan lifetime, CancellationToken ct) => throw new NotSupportedException();

        public Task<(string RawToken, Session Session)?> CreateForActiveAccountAsync(
            Guid userId, AuthLevel level, TimeSpan lifetime, CancellationToken ct)
        {
            if (Failure is not null) throw Failure;
            if (!AccountIsActive) return Task.FromResult<(string RawToken, Session Session)?>(null);

            var now = DateTimeOffset.UtcNow;
            LastLifetime = lifetime;
            Created = new Session(Guid.NewGuid(), userId, new byte[32], Guid.NewGuid(), level, now, now + lifetime);
            return Task.FromResult<(string RawToken, Session Session)?>((Token, Created));
        }

        public Task<Session?> FindByTokenAsync(string rawToken, CancellationToken ct) => throw new NotSupportedException();
        public Task<Session?> RevokeByTokenAsync(string rawToken, CancellationToken ct) => throw new NotSupportedException();
        public Task TouchAsync(Session session, DateTimeOffset now, CancellationToken ct) => throw new NotSupportedException();
    }
}
