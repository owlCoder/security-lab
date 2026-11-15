namespace Platform.Identity.Models;

public sealed record BackupCodeLookupResult(
    bool Exists,
    bool IsUsed,
    DateTimeOffset CreatedAt,
    DateTimeOffset UsedAt)
{
    public static BackupCodeLookupResult NotFound() =>
        new(false, false, default, default);
}
