using Platform.Identity.Interfaces;
using Platform.Identity.Models;

namespace Platform.Identity.Services;

public sealed class BackupCodeRedemptionService(
    IBackupCodeGenerator backupCodeGenerator,
    IBackupCodeConsumption backupCodeConsumption,
    TimeProvider timeProvider)
    : IBackupCodeRedemptionService
{
    public async Task<BackupCodeRedemptionResult> RedeemAsync(
        Guid userId,
        string backupCode,
        CancellationToken cancellationToken = default)
    {
        if (userId == Guid.Empty ||
            backupCode is not { Length: > 0 })
        {
            return BackupCodeRedemptionResult.Rejected();
        }

        if (!backupCodeGenerator.TryComputeHash(
                backupCode, out string codeHash) ||
            codeHash.Length == 0)
        {
            return BackupCodeRedemptionResult.Rejected();
        }

        DateTimeOffset now = timeProvider.GetUtcNow();

        bool consumed = await backupCodeConsumption.TryConsumeAsync(
            userId,
            codeHash,
            now,
            cancellationToken);

        return consumed
            ? BackupCodeRedemptionResult.Success()
            : BackupCodeRedemptionResult.Rejected();
    }
}
