namespace Platform.Identity.Interfaces;

public interface IBackupCodeConsumption
{
    Task<bool> TryConsumeAsync(
        Guid userId,
        string codeHash,
        DateTimeOffset consumedAt,
        CancellationToken cancellationToken = default);
}
