namespace Platform.Api.Models.Identity;

public sealed record SessionInfo(
    Guid Handle,
    DateTimeOffset CreatedAt,
    DateTimeOffset LastActivityAt,
    DateTimeOffset ExpiresAt,
    bool IsRevoked);
