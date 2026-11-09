using Platform.Identity.Models;

namespace Platform.Identity.Interfaces;

public interface IBackupCodeStore
{
    Task<bool> TrySaveAsync(
        Guid userId,
        string codeHash,
        DateTimeOffset createdAt,
        CancellationToken cancellationToken = default);

    Task<BackupCodeLookupResult> FindAsync(
        Guid userId,
        string codeHash,
        CancellationToken cancellationToken = default);
}
