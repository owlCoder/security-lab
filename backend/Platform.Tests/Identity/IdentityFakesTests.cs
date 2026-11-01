using NUnit.Framework;
using Platform.Api.Services.Identity;
using Platform.Api.Domain.Authentication;

namespace Platform.Tests.Identity;

[TestFixture]
[Category("IDN-02")]
public class FakeIdentityContextTests
{
    private static readonly Guid User = Guid.NewGuid();

    [Test]
    public void Default_IsNotAuthenticated()
    {
        var ctx = new FakeIdentityContext();

        Assert.That(ctx.IsAuthenticated, Is.False);
        Assert.That(ctx.UserId, Is.Null);
        Assert.That(ctx.Level, Is.EqualTo(AuthenticationLevel.None));
    }

    [Test]
    public void ActiveSessionWithUser_IsAuthenticated()
    {
        var ctx = new FakeIdentityContext { UserId = User, Level = AuthenticationLevel.Active };

        Assert.That(ctx.IsAuthenticated, Is.True);
        Assert.That(ctx.UserId, Is.EqualTo(User));
    }

    [Test]
    [Category("IDN-06")]
    public void MfaPendingSession_IsNotAuthenticated_EvenWithUserId()
    {
        var ctx = new FakeIdentityContext { UserId = User, Level = AuthenticationLevel.MfaPending };

        Assert.That(ctx.UserId, Is.EqualTo(User));
        Assert.That(ctx.IsAuthenticated, Is.False);
    }

    [Test]
    public void ActiveLevelWithoutUser_IsNotAuthenticated()
    {
        var ctx = new FakeIdentityContext { Level = AuthenticationLevel.Active };

        Assert.That(ctx.IsAuthenticated, Is.False);
    }
}

[TestFixture]
[Category("IDN-02")]
public class InMemorySessionManagementTests
{
    private InMemorySessionManagement _sessions = null!;
    private Guid _alice;
    private Guid _bob;

    [SetUp]
    public void SetUp()
    {
        _sessions = new InMemorySessionManagement();
        _alice = Guid.NewGuid();
        _bob = Guid.NewGuid();
    }

    // ---- RevokeAllForUserAsync ----

    [Test]
    public async Task RevokeAll_RevokesEverySessionOfUser_AndReturnsCount()
    {
        var h1 = _sessions.Seed(_alice);
        var h2 = _sessions.Seed(_alice);
        var h3 = _sessions.Seed(_alice);

        var revoked = _sessions.RevokeAllForUser(_alice, "account blocked");

        Assert.That(revoked, Is.EqualTo(3));
        Assert.That(_sessions.IsActive(h1), Is.False);
        Assert.That(_sessions.IsActive(h2), Is.False);
        Assert.That(_sessions.IsActive(h3), Is.False);
        Assert.That(_sessions.ListForUser(_alice), Is.Empty);
    }

    [Test]
    public async Task RevokeAll_IsIdempotent_SecondCallRevokesNothing()
    {
        _sessions.Seed(_alice);
        _sessions.Seed(_alice);

        _sessions.RevokeAllForUser(_alice, "first");
        var second = _sessions.RevokeAllForUser(_alice, "second");

        Assert.That(second, Is.EqualTo(0));
    }

    [Test]
    public async Task RevokeAll_DoesNotAffectOtherUsers()
    {
        _sessions.Seed(_alice);
        var bobSession = _sessions.Seed(_bob);

        _sessions.RevokeAllForUser(_alice, "account blocked");

        Assert.That(_sessions.IsActive(bobSession), Is.True);
    }

    [Test]
    public async Task RevokeAll_UserWithoutSessions_ReturnsZero()
    {
        var revoked = _sessions.RevokeAllForUser(Guid.NewGuid(), "nothing to do");

        Assert.That(revoked, Is.EqualTo(0));
    }

    // ---- RevokeSessionAsync ----

    [Test]
    public async Task RevokeSession_Own_RevokesOnlyThatSession()
    {
        var target = _sessions.Seed(_alice);
        var other = _sessions.Seed(_alice);

        var result = _sessions.RevokeSession(_alice, target);

        Assert.That(result, Is.True);
        Assert.That(_sessions.IsActive(target), Is.False);
        Assert.That(_sessions.IsActive(other), Is.True);
    }

    [Test]
    public async Task RevokeSession_BelongingToSomeoneElse_ReturnsFalse_SessionStaysActive()
    {
        var bobSession = _sessions.Seed(_bob);

        var result = _sessions.RevokeSession(_alice, bobSession);

        Assert.That(result, Is.False);
        Assert.That(_sessions.IsActive(bobSession), Is.True);
    }

    [Test]
    public async Task RevokeSession_UnknownHandle_LooksTheSameAsForeignSession()
    {
        var bobSession = _sessions.Seed(_bob);

        var foreign = _sessions.RevokeSession(_alice, bobSession);
        var unknown = _sessions.RevokeSession(_alice, Guid.NewGuid());

        Assert.That(unknown, Is.EqualTo(foreign)); // both false, caller cannot probe other users' sessions
    }

    // ---- ListForUserAsync ----

    [Test]
    public async Task List_ReturnsOnlyActiveSessionsOfThatUser()
    {
        var aliceActive = _sessions.Seed(_alice);
        var aliceRevoked = _sessions.Seed(_alice);
        _sessions.Seed(_bob);
        _sessions.RevokeSession(_alice, aliceRevoked);

        var list = _sessions.ListForUser(_alice);

        Assert.That(list, Has.Count.EqualTo(1));
        Assert.That(list[0].Handle, Is.EqualTo(aliceActive));
    }

    [Test]
    public async Task List_DoesNotReturnExpiredSessions()
    {
        _sessions.Seed(_alice, lifetime: TimeSpan.FromSeconds(-1));
        var valid = _sessions.Seed(_alice);

        var list = _sessions.ListForUser(_alice);

        Assert.That(list.Select(s => s.Handle), Is.EquivalentTo(new[] { valid }));
    }

    [Test]
    public void SessionInfo_ExposesNoTokenOrHash()
    {
        var names = typeof(SessionInfo).GetProperties().Select(p => p.Name.ToLowerInvariant());

        Assert.That(names, Has.None.Contains("token"));
        Assert.That(names, Has.None.Contains("hash"));
        Assert.That(names, Has.None.Contains("cookie"));
    }

    // ---- Argument validation ----

    [Test]
    public void EmptyGuids_AreRejected()
    {
        Assert.Throws<ArgumentException>(() => _sessions.RevokeAllForUser(Guid.Empty, "x"));
        Assert.Throws<ArgumentException>(() => _sessions.RevokeSession(Guid.Empty, Guid.NewGuid()));
        Assert.Throws<ArgumentException>(() => _sessions.RevokeSession(_alice, Guid.Empty));
        Assert.Throws<ArgumentException>(() => _sessions.ListForUser(Guid.Empty));
    }
}
