namespace Platform.Identity.Domain;


public sealed class Session
{
	private Session() { } //za EF Core
    
    public Session(Guid id, Guid userId, byte[] tokenHash, Guid sessionHandle,
                   AuthLevel authLevel, DateTimeOffset createdAt, DateTimeOffset expiresAt)
    {
        Id = id;
        UserId = userId;
        TokenHash = tokenHash;
        SessionHandle = sessionHandle;
        AuthLevel = authLevel;
        CreatedAt = createdAt;
        ExpiresAt = expiresAt;
        LastActivityAt = createdAt;
    }

    public Guid Id { get; private set; }
    public Guid UserId { get; private set; }

    /// <summary>SHA-256 hash of the session token. The raw token is never stored.</summary>
    public byte[] TokenHash { get; private set; } = default!;

    /// <summary>Public identifier (not a secret) for listing/revoking sessions (IDN-07).</summary>
    public Guid SessionHandle { get; private set; }

    public AuthLevel AuthLevel { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset ExpiresAt { get; private set; }
    public DateTimeOffset LastActivityAt { get; private set; }
    public DateTimeOffset? RevokedAt { get; private set; }

    public void Touch(DateTimeOffset now)
    {
        LastActivityAt = now;
    }
}
