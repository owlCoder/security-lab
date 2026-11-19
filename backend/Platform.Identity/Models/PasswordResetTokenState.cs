namespace Platform.Identity.Models;

public sealed record PasswordResetTokenState(
    bool Exists,
    DateTimeOffset CreatedAt,
    DateTimeOffset ExpiresAt,
    bool IsUsed);
