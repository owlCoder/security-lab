using Platform.SessionSecurity.Contracts;
using Platform.SessionSecurity.Domain;
using Platform.SessionSecurity.Infrastructure;

// IDN-11 TIM 09
namespace Platform.Tests.SessionSecurity.Infrastructure;

[TestFixture]
public sealed class InMemorySessionRotationStoreTests
{
    private const string OldHash = "OLD-HASH";
    private const string NewHash = "NEW-HASH";
    private const string UserA = "user-a";
    private const int ConcurrentCalls = 50;

    private static readonly DateTimeOffset Now = new(2026, 11, 10, 9, 0, 0, TimeSpan.Zero);
    private static readonly Guid ChainA = new("11111111-2222-3333-4444-555555555555");
    private static readonly Guid ChainB = new("66666666-7777-8888-9999-000000000000");

    [Test]
    public async Task RotateAsync_WhenManyCallsUseSameOldHash_ExactlyOneRotates()
    {
        var store = new InMemorySessionRotationStore();
        store.Add(Session(ChainA, OldHash));

        var statuses = await Task.WhenAll(Enumerable.Range(0, ConcurrentCalls).Select(index =>
            Task.Run(async () => await store.RotateAsync(OldHash, Replacement(ChainA, $"NEW-{index}"), Now))));

        Assert.Multiple(() =>
        {
            Assert.That(statuses.Count(status => status == SessionRotationStoreStatus.Rotated), Is.EqualTo(1));
            Assert.That(
                statuses.Count(status => status == SessionRotationStoreStatus.AlreadyRevoked),
                Is.EqualTo(ConcurrentCalls - 1));
        });
    }

    [Test]
    public async Task RotateAsync_WhenRotated_RevokesOldAndStoresReplacementAsPending()
    {
        var store = new InMemorySessionRotationStore();
        store.Add(Session(ChainA, OldHash));

        var status = await store.RotateAsync(OldHash, Replacement(ChainA, NewHash), Now);

        var old = await store.FindAsync(OldHash);
        var replacement = await store.FindAsync(NewHash);
        Assert.Multiple(() =>
        {
            Assert.That(status, Is.EqualTo(SessionRotationStoreStatus.Rotated));
            Assert.That(old?.RevokedAt, Is.EqualTo(Now));
            Assert.That(replacement?.IsPendingActivation, Is.True);
            Assert.That(replacement?.IsActiveAt(Now), Is.False);
        });
    }

    [Test]
    public async Task RotateAsync_WhenOldHashIsUnknown_ReturnsNotFoundAndStoresNothing()
    {
        var store = new InMemorySessionRotationStore();

        var status = await store.RotateAsync(OldHash, Replacement(ChainA, NewHash), Now);

        await Assert.MultipleAsync(async () =>
        {
            Assert.That(status, Is.EqualTo(SessionRotationStoreStatus.NotFound));
            Assert.That(await store.FindAsync(NewHash), Is.Null);
        });
    }

    [Test]
    public async Task RotateAsync_WhenOldSessionIsPendingActivation_ReturnsNotFoundAndChangesNothing()
    {
        var store = new InMemorySessionRotationStore();
        store.Add(Replacement(ChainA, OldHash));

        var status = await store.RotateAsync(OldHash, Replacement(ChainA, NewHash), Now);

        await Assert.MultipleAsync(async () =>
        {
            Assert.That(status, Is.EqualTo(SessionRotationStoreStatus.NotFound));
            Assert.That(await store.FindAsync(NewHash), Is.Null);
            Assert.That((await store.FindAsync(OldHash))?.IsRevoked, Is.False);
        });
    }

    [Test]
    public async Task RotateAsync_WhenOldSessionIsRevoked_ReturnsAlreadyRevokedAndStoresNothing()
    {
        var store = new InMemorySessionRotationStore();
        store.Add(Session(ChainA, OldHash) with { RevokedAt = Now.AddMinutes(-1) });

        var status = await store.RotateAsync(OldHash, Replacement(ChainA, NewHash), Now);

        await Assert.MultipleAsync(async () =>
        {
            Assert.That(status, Is.EqualTo(SessionRotationStoreStatus.AlreadyRevoked));
            Assert.That(await store.FindAsync(NewHash), Is.Null);
        });
    }

    [Test]
    public async Task RotateAsync_WhenOldSessionReachedAbsoluteExpiry_ReturnsExpiredAndChangesNothing()
    {
        var store = new InMemorySessionRotationStore();
        var old = Session(ChainA, OldHash);
        store.Add(old);

        var status = await store.RotateAsync(OldHash, Replacement(ChainA, NewHash), old.AbsoluteExpiresAt);

        await Assert.MultipleAsync(async () =>
        {
            Assert.That(status, Is.EqualTo(SessionRotationStoreStatus.Expired));
            Assert.That(await store.FindAsync(NewHash), Is.Null);
            Assert.That((await store.FindAsync(OldHash))?.IsRevoked, Is.False);
        });
    }

    [TestCase(true, false)]
    [TestCase(false, true)]
    public async Task RotateAsync_WhenReplacementChangesChainOrAbsoluteExpiry_ThrowsAndChangesNothing(
        bool changeChain, bool extendExpiry)
    {
        var store = new InMemorySessionRotationStore();
        var old = Session(ChainA, OldHash);
        store.Add(old);
        var replacement = Replacement(changeChain ? ChainB : ChainA, NewHash) with
        {
            AbsoluteExpiresAt = extendExpiry ? old.AbsoluteExpiresAt.AddHours(1) : old.AbsoluteExpiresAt,
        };

        Assert.ThrowsAsync<ArgumentException>(async () => await store.RotateAsync(OldHash, replacement, Now));

        await AssertUnchangedAsync(store);
    }

    [Test]
    public async Task RotateAsync_WhenReplacementIsNotPendingActivation_ThrowsAndChangesNothing()
    {
        var store = new InMemorySessionRotationStore();
        store.Add(Session(ChainA, OldHash));

        Assert.ThrowsAsync<ArgumentException>(
            async () => await store.RotateAsync(OldHash, Session(ChainA, NewHash), Now));

        await AssertUnchangedAsync(store);
    }

    [Test]
    public async Task ActivateAsync_WhenReplacementIsPending_MakesItActive()
    {
        var store = new InMemorySessionRotationStore();
        store.Add(Session(ChainA, OldHash));
        await store.RotateAsync(OldHash, Replacement(ChainA, NewHash), Now);

        var activated = await store.ActivateAsync(NewHash, Now);

        var replacement = await store.FindAsync(NewHash);
        Assert.Multiple(() =>
        {
            Assert.That(activated, Is.True);
            Assert.That(replacement?.IsActiveAt(Now), Is.True);
        });
    }

    [TestCase("not pending")]
    [TestCase("revoked")]
    [TestCase("expired")]
    [TestCase("unknown")]
    public async Task ActivateAsync_WhenSessionCannotBeActivated_ReturnsFalseAndChangesNothing(string state)
    {
        var store = new InMemorySessionRotationStore();
        var session = state switch
        {
            "not pending" => Session(ChainA, NewHash),
            "revoked" => Replacement(ChainA, NewHash) with { RevokedAt = Now.AddMinutes(-1) },
            _ => Replacement(ChainA, NewHash),
        };
        if (state != "unknown")
        {
            store.Add(session);
        }

        var activateAt = state == "expired" ? session.AbsoluteExpiresAt : Now;

        var activated = await store.ActivateAsync(NewHash, activateAt);

        await Assert.MultipleAsync(async () =>
        {
            Assert.That(activated, Is.False);
            Assert.That(await store.FindAsync(NewHash), Is.EqualTo(state == "unknown" ? null : session));
        });
    }

    [Test]
    public async Task ActivateAsync_WhenChainWasRevokedAfterRotation_ReturnsFalse()
    {
        var store = new InMemorySessionRotationStore();
        store.Add(Session(ChainA, OldHash));
        await store.RotateAsync(OldHash, Replacement(ChainA, NewHash), Now);
        await store.RevokeChainAsync(ChainA, Now);

        var activated = await store.ActivateAsync(NewHash, Now);

        await Assert.MultipleAsync(async () =>
        {
            Assert.That(activated, Is.False);
            Assert.That((await store.FindAsync(NewHash))?.IsActiveAt(Now), Is.False);
        });
    }

    [Test]
    public async Task RevokeChainAsync_WhenCalledAgain_RevokesNothingAndKeepsFirstRevocationTime()
    {
        var store = new InMemorySessionRotationStore();
        store.Add(Session(ChainA, OldHash));
        await store.RevokeChainAsync(ChainA, Now);

        var revokedAgain = await store.RevokeChainAsync(ChainA, Now.AddMinutes(5));

        await Assert.MultipleAsync(async () =>
        {
            Assert.That(revokedAgain, Is.Zero);
            Assert.That((await store.FindAsync(OldHash))?.RevokedAt, Is.EqualTo(Now));
        });
    }

    [Test]
    public async Task RevokeChainAsync_RevokesOnlySessionsOfThatChain()
    {
        var store = new InMemorySessionRotationStore();
        store.Add(Session(ChainA, "A-1"));
        store.Add(Session(ChainA, "A-2"));
        store.Add(Session(ChainB, "B-1"));

        var revoked = await store.RevokeChainAsync(ChainA, Now);

        await Assert.MultipleAsync(async () =>
        {
            Assert.That(revoked, Is.EqualTo(2));
            Assert.That((await store.FindAsync("A-1"))?.IsRevoked, Is.True);
            Assert.That((await store.FindAsync("A-2"))?.IsRevoked, Is.True);
            Assert.That((await store.FindAsync("B-1"))?.IsRevoked, Is.False);
        });
    }

    [Test]
    public async Task RevokeChainAsync_RevokesPendingReplacementSoItCanNeverBeActivated()
    {
        var store = new InMemorySessionRotationStore();
        store.Add(Session(ChainA, OldHash));
        await store.RotateAsync(OldHash, Replacement(ChainA, NewHash), Now);

        var revoked = await store.RevokeChainAsync(ChainA, Now);

        await Assert.MultipleAsync(async () =>
        {
            Assert.That(revoked, Is.EqualTo(1));
            Assert.That((await store.FindAsync(NewHash))?.IsRevoked, Is.True);
            Assert.That(await store.ActivateAsync(NewHash, Now), Is.False);
        });
    }

    [Test]
    public async Task RevokeAllForSubjectAsync_RevokesSessionsCreatedByRotation()
    {
        var store = new InMemorySessionRotationStore();
        store.Add(Session(ChainA, OldHash));
        store.Add(Session(ChainB, "OTHER-USER") with { SubjectId = "user-b" });
        await store.RotateAsync(OldHash, Replacement(ChainA, NewHash), Now);
        await store.ActivateAsync(NewHash, Now);

        var revoked = await store.RevokeAllForSubjectAsync(UserA, Now);

        await Assert.MultipleAsync(async () =>
        {
            Assert.That(revoked, Is.EqualTo(1));
            Assert.That((await store.FindAsync(NewHash))?.IsRevoked, Is.True);
            Assert.That((await store.FindAsync("OTHER-USER"))?.IsRevoked, Is.False);
        });
    }

    [Test]
    public void Add_WhenTokenHashAlreadyExists_Throws()
    {
        var store = new InMemorySessionRotationStore();
        store.Add(Session(ChainA, OldHash));

        Assert.Throws<InvalidOperationException>(() => store.Add(Session(ChainB, OldHash)));
    }

    private static async Task AssertUnchangedAsync(InMemorySessionRotationStore store)
    {
        await Assert.MultipleAsync(async () =>
        {
            Assert.That(await store.FindAsync(NewHash), Is.Null);
            Assert.That((await store.FindAsync(OldHash))?.IsRevoked, Is.False);
        });
    }

    private static RotatableSession Session(Guid chainId, string tokenHash) =>
        new(chainId, UserA, tokenHash, SessionPrivilegeLevel.Authenticated, Now, Now.AddHours(8));

    private static RotatableSession Replacement(Guid chainId, string tokenHash) =>
        Session(chainId, tokenHash) with { IsPendingActivation = true };
}
