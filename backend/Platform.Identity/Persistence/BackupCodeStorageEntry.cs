namespace Platform.Identity.Persistence;

internal sealed class BackupCodeStorageEntry
{
    private BackupCodeStorageEntry()
    {
    }

    internal BackupCodeStorageEntry(
        Guid userId,
        string codeHash,
        DateTimeOffset createdAt)
    {
        Id = Guid.NewGuid();
        UserId = userId;
        CodeHash = codeHash;
        CreatedAt = createdAt;
    }

    public Guid Id { get; private set; }

    public Guid UserId { get; private set; }

    public string CodeHash { get; private set; } = string.Empty;

    public DateTimeOffset CreatedAt { get; private set; }

    public bool IsUsed { get; private set; }

    public DateTimeOffset UsedAt { get; private set; }
}
