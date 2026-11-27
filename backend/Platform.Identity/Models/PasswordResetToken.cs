
namespace Platform.Identity.Models;

public sealed class PasswordResetToken
{
    private PasswordResetToken()
    {
    }

    public Guid Id { get; private set; }

    public Guid UserId { get; private set; }

    public string TokenHash { get; private set; } = string.Empty;

    public DateTimeOffset CreatedAt { get; private set; }

    public DateTimeOffset ExpiresAt { get; private set; }

    public bool IsUsed { get; private set; }

    public DateTimeOffset UsedAt { get; private set; }

    public static bool TryCreate(
        Guid userId,
        string tokenHash,
        DateTimeOffset createdAt,
        DateTimeOffset expiresAt,
        out PasswordResetToken token)
    {
        token = new PasswordResetToken();

        if (userId == Guid.Empty ||
            tokenHash.AsSpan().Trim().IsEmpty ||
            expiresAt <= createdAt)
        {
            return false;
        }

        token.Id = Guid.NewGuid();
        token.UserId = userId;
        token.TokenHash = tokenHash;
        token.CreatedAt = createdAt;
        token.ExpiresAt = expiresAt;

        return true;
    }

    public bool IsValid()
    {
        return Id != Guid.Empty &&
               UserId != Guid.Empty &&
               !TokenHash.AsSpan().Trim().IsEmpty &&
               ExpiresAt > CreatedAt;
    }

    public bool IsExpired(DateTimeOffset now)
    {
        return now >= ExpiresAt;
    }

    public bool CanBeUsed(DateTimeOffset now)
    {
        return IsValid() &&
               !IsUsed &&
               now >= CreatedAt &&
               !IsExpired(now);
    }

    public bool TryMarkAsUsed(DateTimeOffset now)
    {
        if (!CanBeUsed(now))
        {
            return false;
        }

        IsUsed = true;
        UsedAt = now;

        return true;
    }
}
