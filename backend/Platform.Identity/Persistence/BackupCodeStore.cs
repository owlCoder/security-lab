using Npgsql;
using Microsoft.EntityFrameworkCore;
using Platform.Identity.Interfaces;
using Platform.Identity.Models;

namespace Platform.Identity.Persistence;

public sealed class BackupCodeStore(
    PlatformIdentityDbContext dbContext)
    : IBackupCodeStore, IBackupCodeConsumption
{
    public async Task<bool> TrySaveAsync(
        Guid userId,
        string codeHash,
        DateTimeOffset createdAt,
        CancellationToken cancellationToken = default)
    {
        if (userId == Guid.Empty ||
            !IsValidCodeHash(codeHash) ||
            createdAt == default)
        {
            return false;
        }

        var entry = new BackupCodeStorageEntry(
            userId,
            codeHash,
            createdAt);
        dbContext.Set<BackupCodeStorageEntry>().Add(entry);

        try
        {
            int saved = await dbContext.SaveChangesAsync(
                cancellationToken);

            return saved > 0;
        }
        catch (DbUpdateException exception) when (
            exception.InnerException is PostgresException postgresException &&
            (
                (postgresException.SqlState == PostgresErrorCodes.UniqueViolation &&
                postgresException.ConstraintName == "IX_backup_codes_user_id_code_hash")
                ||
                (postgresException.SqlState == PostgresErrorCodes.ForeignKeyViolation &&
                postgresException.ConstraintName == "FK_backup_codes_accounts_user_id")
            ))
        {
            dbContext.Entry(entry).State = EntityState.Detached;
            return false;
        }

    }

    public async Task<BackupCodeLookupResult> FindAsync(
        Guid userId,
        string codeHash,
        CancellationToken cancellationToken = default)
    {
        if (userId == Guid.Empty ||
            !IsValidCodeHash(codeHash))
        {
            return BackupCodeLookupResult.NotFound();
        }

        var records = await dbContext
            .Set<BackupCodeStorageEntry>()
            .AsNoTracking()
            .Where(x =>
                x.UserId == userId &&
                x.CodeHash == codeHash)
            .Select(x => new BackupCodeLookupResult(
                true,
                x.IsUsed,
                x.CreatedAt,
                x.UsedAt))
            .Take(1)
            .ToListAsync(cancellationToken);

        return records.Count == 0
            ? BackupCodeLookupResult.NotFound()
            : records[0];
    }

    public async Task<bool> TryConsumeAsync(
        Guid userId,
        string codeHash,
        DateTimeOffset consumedAt,
        CancellationToken cancellationToken = default)
    {
        if (userId == Guid.Empty ||
            !IsValidCodeHash(codeHash) ||
            consumedAt == default)
        {
            return false;
        }

        int updatedRows = await dbContext
            .Set<BackupCodeStorageEntry>()
            .Where(x =>
                x.UserId == userId &&
                x.CodeHash == codeHash &&
                !x.IsUsed &&
                x.CreatedAt <= consumedAt)
            .ExecuteUpdateAsync(
                setters => setters
                    .SetProperty(x => x.IsUsed, true)
                    .SetProperty(x => x.UsedAt, consumedAt),
                cancellationToken);

        return updatedRows == 1;
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
