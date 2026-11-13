namespace Platform.Identity.Models;

public sealed class BackupCodeRecord
{
    private BackupCodeRecord()
    {
    }

    public Guid Id { get; private set; }

    public Guid UserId { get; private set; }

    public string CodeHash { get; private set; } = string.Empty;

    public DateTimeOffset CreatedAt { get; private set; }

    public bool IsUsed { get; private set; }

    public DateTimeOffset UsedAt { get; private set; }

    public static bool TryCreate(
        Guid userId,
        string codeHash,
        DateTimeOffset createdAt,
        out BackupCodeRecord record)
    {
        record = new BackupCodeRecord();

        if (userId == Guid.Empty ||
            !IsValidCodeHash(codeHash) ||
            createdAt == default)
        {
            return false;
        }

        record.Id = Guid.NewGuid();
        record.UserId = userId;
        record.CodeHash = codeHash;
        record.CreatedAt = createdAt;

        return true;
    }

    public bool IsValid()
    {
        return Id != Guid.Empty &&
               UserId != Guid.Empty &&
               IsValidCodeHash(CodeHash) &&
               CreatedAt != default;
    }

    public bool CanBeUsed(DateTimeOffset now)
    {
        return IsValid() &&
               !IsUsed &&
               now >= CreatedAt;
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
    private static bool IsValidCodeHash(string codeHash)
    {
        if (string.IsNullOrWhiteSpace(codeHash) ||
            codeHash.Length != 64)
        {
            return false;
        }

        foreach (char character in codeHash)
        {
            if (!char.IsAsciiHexDigit(character))
            {
                return false;
            }
        }

        return true;
    }

}
