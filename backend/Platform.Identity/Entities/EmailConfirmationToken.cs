namespace Platform.Identity.Entities;

public sealed class EmailConfirmationToken(
    Guid id,
    Guid userId,
    byte[] tokenHash,
    DateTimeOffset createdAt,
    DateTimeOffset expiresAt)
{
    public Guid Id { get; private set; } = id;
    public Guid UserId { get; private set; } = userId;
    public byte[] TokenHash { get; private set; } = tokenHash;
    public DateTimeOffset CreatedAt { get; private set; } = createdAt;
    public DateTimeOffset ExpiresAt { get; private set; } = expiresAt;
    public DateTimeOffset? ConsumedAt { get; private set; }
    public DateTimeOffset? RevokedAt { get; private set; }
}
