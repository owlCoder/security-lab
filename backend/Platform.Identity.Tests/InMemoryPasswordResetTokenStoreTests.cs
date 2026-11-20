using NUnit.Framework;
using Platform.Identity.Interfaces;
using Platform.Identity.Models;
using Platform.Identity.Services;

namespace Platform.Identity.Tests;

[TestFixture]
public sealed class InMemoryPasswordResetTokenStoreTests
{
    private static readonly DateTimeOffset CreatedAt =
        new(2026, 11, 1, 12, 0, 0, TimeSpan.Zero);

    private static readonly DateTimeOffset ExpiresAt =
        CreatedAt.AddMinutes(30);

    private static readonly DateTimeOffset ValidTime =
        CreatedAt.AddMinutes(10);

    private const string TokenHash = "sha256-test-hash-1";

    private static PasswordResetToken CreateToken(string tokenHash)
    {
        bool created = PasswordResetToken.TryCreate(
            Guid.NewGuid(),
            tokenHash,
            CreatedAt,
            ExpiresAt,
            out var token);

        Assert.That(created, Is.True);

        return token;
    }

    [Test]
    public async Task TrySaveAsync_ValidToken_SavesSuccessfully()
    {
        var store = new InMemoryPasswordResetTokenStore();
        var token = CreateToken(TokenHash);

        bool saved = await store.TrySaveAsync(token);

        Assert.That(saved, Is.True);
    }

    [Test]
    public async Task FindByHashAsync_ExistingToken_ReturnsToken()
    {
        var store = new InMemoryPasswordResetTokenStore();
        var token = CreateToken(TokenHash);

        await store.TrySaveAsync(token);

        var result = await store.FindByHashAsync(TokenHash);

        Assert.That(
            result,
            Is.TypeOf<PasswordResetTokenLookupResult.Found>());

        var found = (PasswordResetTokenLookupResult.Found)result;

        Assert.That(found.Token.Id, Is.EqualTo(token.Id));
        Assert.That(found.Token.UserId, Is.EqualTo(token.UserId));
    }

    [Test]
    public async Task FindByHashAsync_UnknownToken_ReturnsNotFound()
    {
        var store = new InMemoryPasswordResetTokenStore();

        var result = await store.FindByHashAsync("unknown-hash");

        Assert.That(
            result,
            Is.TypeOf<PasswordResetTokenLookupResult.NotFound>());
    }

    [Test]
    public async Task TrySaveAsync_DuplicateHash_IsRejected()
    {
        var store = new InMemoryPasswordResetTokenStore();

        var firstToken = CreateToken(TokenHash);
        var secondToken = CreateToken(TokenHash);

        bool firstSaved = await store.TrySaveAsync(firstToken);
        bool secondSaved = await store.TrySaveAsync(secondToken);

        Assert.Multiple(() =>
        {
            Assert.That(firstSaved, Is.True);
            Assert.That(secondSaved, Is.False);
        });
    }

    [Test]
    public async Task TrySaveAsync_InvalidToken_IsRejected()
    {
        var store = new InMemoryPasswordResetTokenStore();

        bool created = PasswordResetToken.TryCreate(
            Guid.NewGuid(),
            " ",
            CreatedAt,
            ExpiresAt,
            out var invalidToken);

        bool saved = await store.TrySaveAsync(invalidToken);

        Assert.Multiple(() =>
        {
            Assert.That(created, Is.False);
            Assert.That(saved, Is.False);
        });
    }

    [Test]
    public async Task TryConsumeAsync_ValidToken_SucceedsOnce()
    {
        var store = new InMemoryPasswordResetTokenStore();

        await store.TrySaveAsync(CreateToken(TokenHash));

        bool firstUse = await store.TryConsumeAsync(
            TokenHash, ValidTime);

        bool secondUse = await store.TryConsumeAsync(
            TokenHash, ValidTime);

        Assert.Multiple(() =>
        {
            Assert.That(firstUse, Is.True);
            Assert.That(secondUse, Is.False);
        });
    }

    [Test]
    public async Task TryConsumeAsync_ExpiredToken_IsRejected()
    {
        var store = new InMemoryPasswordResetTokenStore();

        await store.TrySaveAsync(CreateToken(TokenHash));

        bool consumed = await store.TryConsumeAsync(
            TokenHash, ExpiresAt);

        Assert.That(consumed, Is.False);
    }

    [Test]
    public async Task TryConsumeAsync_UnknownToken_IsRejected()
    {
        var store = new InMemoryPasswordResetTokenStore();

        bool consumed = await store.TryConsumeAsync(
            "unknown-hash", ValidTime);

        Assert.That(consumed, Is.False);
    }

    [Test]
    public async Task TryConsumeAsync_ConcurrentRequests_OnlyOneSucceeds()
    {
        var store = new InMemoryPasswordResetTokenStore();

        await store.TrySaveAsync(CreateToken(TokenHash));

        var attempts = Enumerable.Range(0, 20)
            .Select(_ => Task.Run(
                async () => await store.TryConsumeAsync(
                    TokenHash, ValidTime)));

        bool[] results = await Task.WhenAll(attempts);

        Assert.That(results.Count(success => success), Is.EqualTo(1));
    } 
[Test]
public async Task TokenStore_ChangesToOriginalToken_DoNotAffectStoredState()
{
    var store = new InMemoryPasswordResetTokenStore();
    var originalToken = CreateToken(TokenHash);

    bool saved = await store.TrySaveAsync(originalToken);

    Assert.That(saved, Is.True);

    // Menjamo originalni objekat nakon cuvanja.
    bool originalConsumed = originalToken.TryMarkAsUsed(ValidTime);

    Assert.That(originalConsumed, Is.True);

    // Skladiste mora zadrzati svoje nezavisno stanje.
    var before = await store.FindByHashAsync(TokenHash);

    Assert.That(
        before,
        Is.TypeOf<PasswordResetTokenLookupResult.Found>());

    var beforeSnapshot =
        (PasswordResetTokenLookupResult.Found)before;

    Assert.That(beforeSnapshot.Token.IsUsed, Is.False);

    // Token u skladistu se koristi nezavisno od originala.
    bool consumed = await store.TryConsumeAsync(
        TokenHash,
        ValidTime);

    Assert.That(consumed, Is.True);

    var after = await store.FindByHashAsync(TokenHash);

    Assert.That(
        after,
        Is.TypeOf<PasswordResetTokenLookupResult.Found>());

    var afterSnapshot =
        (PasswordResetTokenLookupResult.Found)after;

    Assert.Multiple(() =>
    {
        Assert.That(beforeSnapshot.Token.IsUsed, Is.False);
        Assert.That(afterSnapshot.Token.IsUsed, Is.True);
        Assert.That(afterSnapshot.Token.UsedAt, Is.EqualTo(ValidTime));
    });
}

}
