using Microsoft.EntityFrameworkCore;
using Platform.Api.Enums.Identity;
using Platform.Api.Models.Identity;
using Platform.Api.Services.Identity;
using Platform.Identity.Entities;
using Platform.Identity.Enums;
using Platform.Identity.Persistence;
using Platform.Identity.Services;

namespace Platform.Identity.Tests.Services;

/// IDN-07: ValidateAsync runs on every protected request and reads the saved state of the session.
[TestFixture]
[Category("IDN-07")]
public class SessionManagerValidationTests
{
    private static readonly TimeSpan Lifetime = TimeSpan.FromHours(8);

    private DbContextOptions<PlatformIdentityDbContext> _options = null!;
    private MovableClock _clock = null!;
    private PlatformIdentityDbContext _db = null!;
    private SessionManager _manager = null!;

    [SetUp]
    public void SetUp()
    {
        _options = new DbContextOptionsBuilder<PlatformIdentityDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

        _clock = new MovableClock();
        _db = new PlatformIdentityDbContext(_options);
        _manager = new SessionManager(_db, _clock);
    }

    [TearDown]
    public void TearDown() => _db.Dispose();

    [Test]
    public async Task ActiveSession_IsValid_AndLastActivityIsUpdated()
    {
        var userId = Guid.NewGuid();
        var (token, session) = await CreateSessionAsync(userId);
        _clock.Advance(TimeSpan.FromMinutes(5));

        var result = await _manager.ValidateAsync(token);

        Assert.That(result, Is.EqualTo(new ValidateSession(session.SessionHandle, userId, AuthenticationLevel.Active)));
        Assert.That(ReadSaved(session.Id).LastActivityAt, Is.EqualTo(_clock.GetUtcNow()));
    }

    [Test]
    public async Task MfaPendingSession_IsReturnedWithItsLevel_SoThePolicyCanRefuseIt()
    {
        var (token, _) = await CreateSessionAsync(Guid.NewGuid(), AuthLevel.MfaPending);

        var result = await _manager.ValidateAsync(token);

        Assert.That(result!.Level, Is.EqualTo(AuthenticationLevel.MfaPending));
    }

    [Test]
    public async Task RevokedSession_IsRejected_AndLastActivityIsNotUpdated()
    {
        var (token, session) = await CreateSessionAsync(Guid.NewGuid());
        await RevokeAsync(token);
        var activityBefore = ReadSaved(session.Id).LastActivityAt;
        _clock.Advance(TimeSpan.FromMinutes(5));

        var result = await _manager.ValidateAsync(token);

        Assert.That(result, Is.Null);
        Assert.That(ReadSaved(session.Id).LastActivityAt, Is.EqualTo(activityBefore));
    }

    [Test]
    public async Task SessionRevokedAfterItWasUsed_IsRejectedOnTheNextRequest()
    {
        // Same token, another tab or a copy of the token: the check always reads the saved state.
        var (token, _) = await CreateSessionAsync(Guid.NewGuid());
        Assert.That(await _manager.ValidateAsync(token), Is.Not.Null);

        await RevokeAsync(token);

        using var nextRequestDb = new PlatformIdentityDbContext(_options);
        Assert.That(await new SessionManager(nextRequestDb, _clock).ValidateAsync(token), Is.Null);
    }

    [Test]
    public async Task ExpiredSession_IsRejected_AtTheExactExpiryTime()
    {
        var (token, _) = await CreateSessionAsync(Guid.NewGuid());
        _clock.Advance(Lifetime);

        Assert.That(await _manager.ValidateAsync(token), Is.Null);
    }

    [Test]
    public async Task SessionJustBeforeExpiry_IsStillValid()
    {
        var (token, _) = await CreateSessionAsync(Guid.NewGuid());
        _clock.Advance(Lifetime - TimeSpan.FromSeconds(1));

        Assert.That(await _manager.ValidateAsync(token), Is.Not.Null);
    }

    [Test]
    public async Task RevokingOneSession_LeavesOtherSessionsOfTheUserValid()
    {
        var userId = Guid.NewGuid();
        var (revokedToken, _) = await CreateSessionAsync(userId);
        var (otherToken, _) = await CreateSessionAsync(userId);

        await RevokeAsync(revokedToken);

        Assert.That(await _manager.ValidateAsync(revokedToken), Is.Null);
        Assert.That(await _manager.ValidateAsync(otherToken), Is.Not.Null);
    }

    [TestCase("unknown-token")]
    [TestCase("")]
    [TestCase("   ")]
    [TestCase("' OR '1'='1")]
    [TestCase("'; DROP TABLE sessions; --")]
    [TestCase("\0")]
    [TestCase("šđčćž-токен-🔒")]
    public async Task UnknownBlankOrMaliciousToken_IsRejected(string token)
    {
        await CreateSessionAsync(Guid.NewGuid());

        Assert.That(await _manager.ValidateAsync(token), Is.Null);
    }

    [Test]
    public async Task VeryLongToken_IsRejected()
    {
        await CreateSessionAsync(Guid.NewGuid());

        Assert.That(await _manager.ValidateAsync(new string('a', 100_000)), Is.Null);
    }

    [Test]
    public async Task TokenWithChangedCaseOrExtraCharacter_IsRejected()
    {
        var (token, _) = await CreateSessionAsync(Guid.NewGuid());

        Assert.That(await _manager.ValidateAsync(token.ToUpperInvariant()), Is.Null);
        Assert.That(await _manager.ValidateAsync(token + "x"), Is.Null);
        Assert.That(await _manager.ValidateAsync(token[..^1]), Is.Null);
    }

    // Sessions are created and revoked through their own contexts, like separate requests would do.
    private async Task<(string Token, Session Session)> CreateSessionAsync(Guid userId, AuthLevel level = AuthLevel.Active)
    {
        using var db = new PlatformIdentityDbContext(_options);

        // IDN-03 / SEC-03: ValidateAsync refuses a session whose account is missing or not Active,
        // so every session here belongs to a persisted Active account.
        if (!await db.Accounts.AnyAsync(account => account.Id == userId))
        {
            db.Accounts.Add(new UserAccount(userId, $"{userId:N}@example.test"));
            await db.SaveChangesAsync();
        }

        return await new SessionStore(db, _clock).CreateAsync(userId, level, Lifetime, CancellationToken.None);
    }

    private async Task RevokeAsync(string token)
    {
        using var db = new PlatformIdentityDbContext(_options);
        await new SessionStore(db, _clock).RevokeByTokenAsync(token, CancellationToken.None);
    }

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
