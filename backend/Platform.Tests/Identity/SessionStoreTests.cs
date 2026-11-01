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

    private PlatformIdentityDbContext _db = null!;
    private SessionStore _store = null!;

    [SetUp]
    public void SetUp()
    {
        var options = new DbContextOptionsBuilder<PlatformIdentityDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

        _db = new PlatformIdentityDbContext(options);
        _store = new SessionStore(_db, TimeProvider.System);
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
    public async Task RevokeByToken_SetsRevokedAt_AndReturnsTheSession()
    {
        var (token, session) = await _store.CreateAsync(Guid.NewGuid(), AuthLevel.Active, Lifetime, CancellationToken.None);

        var revoked = await _store.RevokeByTokenAsync(token, CancellationToken.None);

        Assert.That(revoked!.Id, Is.EqualTo(session.Id));
        Assert.That((await _store.FindByTokenAsync(token, CancellationToken.None))!.RevokedAt, Is.Not.Null);
    }

    [Test]
    public async Task RevokeByToken_Twice_ReturnsNull_AndKeepsTheFirstTime()
    {
        var (token, session) = await _store.CreateAsync(Guid.NewGuid(), AuthLevel.Active, Lifetime, CancellationToken.None);
        await _store.RevokeByTokenAsync(token, CancellationToken.None);
        var firstTime = session.RevokedAt;

        var second = await _store.RevokeByTokenAsync(token, CancellationToken.None);

        Assert.That(second, Is.Null);
        Assert.That(session.RevokedAt, Is.EqualTo(firstTime));
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
        var (otherToken, _) = await _store.CreateAsync(userId, AuthLevel.Active, Lifetime, CancellationToken.None);

        await _store.RevokeByTokenAsync(token, CancellationToken.None);

        Assert.That((await _store.FindByTokenAsync(otherToken, CancellationToken.None))!.RevokedAt, Is.Null);
    }

    [Test]
    public async Task RevokeByToken_WorksForMfaPendingSession()
    {
        var (token, _) = await _store.CreateAsync(Guid.NewGuid(), AuthLevel.MfaPending, Lifetime, CancellationToken.None);

        Assert.That(await _store.RevokeByTokenAsync(token, CancellationToken.None), Is.Not.Null);
    }
}
