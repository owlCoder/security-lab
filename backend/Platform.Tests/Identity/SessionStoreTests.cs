using Microsoft.EntityFrameworkCore;
using Platform.Identity.Data;
using Platform.Identity.Domain;
using Platform.Identity.Services;

namespace Platform.Tests.Identity;

[TestFixture]
[Category("US-03")]
public class SessionStoreTests
{
    private static readonly TimeSpan Lifetime = TimeSpan.FromHours(8);

    private DbContextOptions<PlatformIdentityDbContext> _options = null!;
    private MovableClock _clock = null!;
    private PlatformIdentityDbContext _db = null!;
    private SessionStore _store = null!;

    [SetUp]
    public void SetUp()
    {
        _options = new DbContextOptionsBuilder<PlatformIdentityDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

        _clock = new MovableClock();
        _db = new PlatformIdentityDbContext(_options);
        _store = new SessionStore(_db, _clock);
    }

    [TearDown]
    public void TearDown() => _db.Dispose();

    [Test]
    public async Task Create_ReturnsNewTokenEveryTime_AndStoresOnlyItsHash()
    {
        var userId = Guid.NewGuid();

        var (first, session) = await _store.CreateAsync(userId, AuthLevel.Active, Lifetime, CancellationToken.None);
        var (second, _) = await _store.CreateAsync(userId, AuthLevel.Active, Lifetime, CancellationToken.None);

        Assert.That(first, Is.Not.EqualTo(second));
        Assert.That(session.TokenHash, Is.EqualTo(SessionTokenGenerator.Hash(first)));
        Assert.That(session.TokenHash, Is.Not.EqualTo(System.Text.Encoding.UTF8.GetBytes(first)));
    }

    [Test]
    public async Task RevokeByToken_SavesRevokedAt_AndReturnsTheSession()
    {
        var (token, session) = await _store.CreateAsync(Guid.NewGuid(), AuthLevel.Active, Lifetime, CancellationToken.None);

        var revoked = await _store.RevokeByTokenAsync(token, CancellationToken.None);

        Assert.That(revoked!.Id, Is.EqualTo(session.Id));
        Assert.That(ReadSaved(session.Id).RevokedAt, Is.EqualTo(_clock.GetUtcNow()));
    }

    [Test]
    public async Task RevokeByToken_Twice_ReturnsNull_AndKeepsTheFirstTime()
    {
        var (token, session) = await _store.CreateAsync(Guid.NewGuid(), AuthLevel.Active, Lifetime, CancellationToken.None);
        await _store.RevokeByTokenAsync(token, CancellationToken.None);
        var firstTime = ReadSaved(session.Id).RevokedAt;
        _clock.Advance(TimeSpan.FromMinutes(1));

        var second = await _store.RevokeByTokenAsync(token, CancellationToken.None);

        Assert.That(second, Is.Null);
        Assert.That(ReadSaved(session.Id).RevokedAt, Is.EqualTo(firstTime));
    }

    [Test]
    public async Task RevokeByToken_AlreadyRevokedByAnotherRequest_ReturnsNull_AndKeepsTheirTime()
    {
        var (token, session) = await _store.CreateAsync(Guid.NewGuid(), AuthLevel.Active, Lifetime, CancellationToken.None);

        // This request has already read the session as not revoked. Then the other request wins.
        var otherTime = _clock.GetUtcNow();
        using (var other = new PlatformIdentityDbContext(_options))
        {
            await new SessionStore(other, _clock).RevokeByTokenAsync(token, CancellationToken.None);
        }
        _clock.Advance(TimeSpan.FromMinutes(1));

        var revoked = await _store.RevokeByTokenAsync(token, CancellationToken.None);

        Assert.That(revoked, Is.Null);
        Assert.That(ReadSaved(session.Id).RevokedAt, Is.EqualTo(otherTime));
    }

    [Test]
    public async Task RevokeByToken_ExpiredSession_ReturnsNull_AndChangesNothing()
    {
        var (token, session) = await _store.CreateAsync(Guid.NewGuid(), AuthLevel.Active, Lifetime, CancellationToken.None);
        _clock.Advance(Lifetime);

        var revoked = await _store.RevokeByTokenAsync(token, CancellationToken.None);

        Assert.That(revoked, Is.Null);
        Assert.That(ReadSaved(session.Id).RevokedAt, Is.Null);
    }

    [TestCase("unknown-token")]
    [TestCase("")]
    [TestCase("   ")]
    public async Task RevokeByToken_UnknownOrEmptyToken_ReturnsNull(string token)
    {
        Assert.That(await _store.RevokeByTokenAsync(token, CancellationToken.None), Is.Null);
    }

    [Test]
    public async Task RevokeByToken_LeavesOtherSessionsOfTheUserValid()
    {
        var userId = Guid.NewGuid();
        var (token, _) = await _store.CreateAsync(userId, AuthLevel.Active, Lifetime, CancellationToken.None);
        var (_, other) = await _store.CreateAsync(userId, AuthLevel.Active, Lifetime, CancellationToken.None);

        await _store.RevokeByTokenAsync(token, CancellationToken.None);

        Assert.That(ReadSaved(other.Id).RevokedAt, Is.Null);
    }

    [Test]
    public async Task RevokeByToken_WorksForMfaPendingSession()
    {
        var (token, session) = await _store.CreateAsync(Guid.NewGuid(), AuthLevel.MfaPending, Lifetime, CancellationToken.None);

        Assert.That(await _store.RevokeByTokenAsync(token, CancellationToken.None), Is.Not.Null);
        Assert.That(ReadSaved(session.Id).RevokedAt, Is.Not.Null);
    }

    // Reads through a new context, so the test sees what was saved and not a tracked object.
    private Session ReadSaved(Guid id)
    {
        using var fresh = new PlatformIdentityDbContext(_options);
        return fresh.Sessions.AsNoTracking().Single(s => s.Id == id);
    }

    private sealed class MovableClock : TimeProvider
    {
        private DateTimeOffset _now = new(2026, 11, 1, 12, 0, 0, TimeSpan.Zero);

        public override DateTimeOffset GetUtcNow() => _now;
        public void Advance(TimeSpan by) => _now += by;
    }
}
