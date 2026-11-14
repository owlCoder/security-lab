namespace Platform.Audit.Contracts;

public interface IMfaBackupCodeAuditService
{
    ValueTask<AuditWriteResult> RecordGeneratedAsync(
        Guid userId,
        CancellationToken cancellationToken = default);

    ValueTask<AuditWriteResult> RecordRedeemedAsync(
        Guid userId,
        CancellationToken cancellationToken = default);

    ValueTask<AuditWriteResult> RecordRejectedAsync(
        Guid userId,
        CancellationToken cancellationToken = default);

    ValueTask<AuditWriteResult> RecordRevokedAsync(
        Guid userId,
        CancellationToken cancellationToken = default);
}
