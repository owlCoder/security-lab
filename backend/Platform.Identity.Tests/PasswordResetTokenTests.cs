
using Platform.Identity.Models;

namespace Platform.Identity.Tests;

[TestFixture]
public sealed class PasswordResetTokenTests
{
    private static readonly DateTimeOffset CreatedAt =
        new(2026, 11, 5, 12, 0, 0, TimeSpan.Zero);

    private static PasswordResetToken CreateToken()
    {
        var created = PasswordResetToken.TryCreate(
            Guid.NewGuid(),
            "test-token-hash",
            CreatedAt,
            CreatedAt.AddMinutes(15),
            out var token);

        Assert.That(created, Is.True);
        return token;
    }

    [Test]
    public void New_token_has_correct_initial_state()
    {
        var userId = Guid.NewGuid();
        var expiresAt = CreatedAt.AddMinutes(15);

        var created = PasswordResetToken.TryCreate(
            userId,
            "test-token-hash",
            CreatedAt,
            expiresAt,
            out var token);

        Assert.That(created, Is.True);
        Assert.That(token.IsValid(), Is.True);
        Assert.That(token.Id, Is.Not.EqualTo(Guid.Empty));
        Assert.That(token.UserId, Is.EqualTo(userId));
        Assert.That(token.TokenHash, Is.EqualTo("test-token-hash"));
        Assert.That(token.CreatedAt, Is.EqualTo(CreatedAt));
        Assert.That(token.ExpiresAt, Is.EqualTo(expiresAt));
        Assert.That(token.IsUsed, Is.False);
        Assert.That(token.UsedAt,
            Is.EqualTo(default(DateTimeOffset)));
    }

    [Test]
    public void Valid_token_can_be_used()
    {
        var token = CreateToken();
        var now = CreatedAt.AddMinutes(5);

        Assert.That(token.IsValid(), Is.True);
        Assert.That(token.IsExpired(now), Is.False);
        Assert.That(token.CanBeUsed(now), Is.True);
    }

    [Test]
    public void Expired_token_cannot_be_used()
    {
        var token = CreateToken();
        var now = CreatedAt.AddMinutes(16);

        Assert.That(token.IsExpired(now), Is.True);
        Assert.That(token.CanBeUsed(now), Is.False);
        Assert.That(token.TryMarkAsUsed(now), Is.False);
        Assert.That(token.IsUsed, Is.False);
    }

    [Test]
    public void Token_expires_at_exact_expiration_time()
    {
        var token = CreateToken();
        var now = token.ExpiresAt;

        Assert.That(token.IsExpired(now), Is.True);
        Assert.That(token.CanBeUsed(now), Is.False);
    }

    [Test]
    public void Token_cannot_be_used_before_creation()
    {
        var token = CreateToken();
        var now = CreatedAt.AddMinutes(-1);

        Assert.That(token.CanBeUsed(now), Is.False);
        Assert.That(token.TryMarkAsUsed(now), Is.False);
        Assert.That(token.IsUsed, Is.False);
    }

    [Test]
    public void Token_can_be_marked_as_used_once()
    {
        var token = CreateToken();
        var now = CreatedAt.AddMinutes(5);

        var result = token.TryMarkAsUsed(now);

        Assert.That(result, Is.True);
        Assert.That(token.IsUsed, Is.True);
        Assert.That(token.UsedAt, Is.EqualTo(now));
        Assert.That(token.CanBeUsed(now), Is.False);
    }

    [Test]
    public void Used_token_cannot_be_used_again()
    {
        var token = CreateToken();
        var firstUse = CreatedAt.AddMinutes(5);
        var secondUse = CreatedAt.AddMinutes(6);

        Assert.That(token.TryMarkAsUsed(firstUse), Is.True);
        Assert.That(token.TryMarkAsUsed(secondUse), Is.False);
        Assert.That(token.IsUsed, Is.True);
        Assert.That(token.UsedAt, Is.EqualTo(firstUse));
    }

    [Test]
    public void Empty_user_id_is_rejected()
    {
        var created = PasswordResetToken.TryCreate(
            Guid.Empty,
            "test-token-hash",
            CreatedAt,
            CreatedAt.AddMinutes(15),
            out var token);

        Assert.That(created, Is.False);
        Assert.That(token.IsValid(), Is.False);
        Assert.That(token.CanBeUsed(CreatedAt), Is.False);
    }

    [TestCase("")]
    [TestCase("   ")]
    public void Empty_token_hash_is_rejected(string hash)
    {
        var created = PasswordResetToken.TryCreate(
            Guid.NewGuid(),
            hash,
            CreatedAt,
            CreatedAt.AddMinutes(15),
            out var token);

        Assert.That(created, Is.False);
        Assert.That(token.IsValid(), Is.False);
        Assert.That(token.CanBeUsed(CreatedAt), Is.False);
    }

    [TestCase(0)]
    [TestCase(-1)]
    public void Invalid_expiration_is_rejected(int minutes)
    {
        var created = PasswordResetToken.TryCreate(
            Guid.NewGuid(),
            "test-token-hash",
            CreatedAt,
            CreatedAt.AddMinutes(minutes),
            out var token);

        Assert.That(created, Is.False);
        Assert.That(token.IsValid(), Is.False);
        Assert.That(token.CanBeUsed(CreatedAt), Is.False);
    }
}
