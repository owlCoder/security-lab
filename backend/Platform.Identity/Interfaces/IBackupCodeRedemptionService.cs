using Platform.Identity.Models;

namespace Platform.Identity.Interfaces;

public interface IBackupCodeRedemptionService
{
    Task<BackupCodeRedemptionResult> RedeemAsync(
        Guid userId,
        string backupCode,
        CancellationToken cancellationToken = default);
}
