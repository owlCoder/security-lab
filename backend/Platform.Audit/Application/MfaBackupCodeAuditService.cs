using Platform.Audit.Contracts;

namespace Platform.Audit.Application;

public sealed class MfaBackupCodeAuditService(
    IAuditWriter auditWriter)
    : IMfaBackupCodeAuditService
{
    public ValueTask<AuditWriteResult> RecordGeneratedAsync(
        Guid userId,
        CancellationToken cancellationToken = default)
    {
        return WriteForAccountAsync(
            AuditEventTypes.AuthMfaBackupCodesGenerated,
            AuditOutcome.Success,
            userId,
            cancellationToken);
    }

    public ValueTask<AuditWriteResult> RecordRedeemedAsync(
        Guid userId,
        CancellationToken cancellationToken = default)
    {
        return WriteForAccountAsync(
            AuditEventTypes.AuthMfaBackupCodeRedeemed,
            AuditOutcome.Success,
            userId,
            cancellationToken);
    }

    public ValueTask<AuditWriteResult> RecordRejectedAsync(
        Guid userId,
        CancellationToken cancellationToken = default)
    {
        if (userId == Guid.Empty)
        {
            var anonymousDraft = new AuditEventDraft(
                AuditEventTypes.AuthMfaBackupCodeRejected,
                AuditOutcome.Denied);

            return auditWriter.WriteAsync(
                anonymousDraft,
                cancellationToken);
        }

        return WriteForAccountAsync(
            AuditEventTypes.AuthMfaBackupCodeRejected,
            AuditOutcome.Denied,
            userId,
            cancellationToken);
    }

    public ValueTask<AuditWriteResult> RecordRevokedAsync(
        Guid userId,
        CancellationToken cancellationToken = default)
    {
        return WriteForAccountAsync(
            AuditEventTypes.AuthMfaBackupCodesRevoked,
            AuditOutcome.Success,
            userId,
            cancellationToken);
    }

    private ValueTask<AuditWriteResult> WriteForAccountAsync(
        string eventType,
        AuditOutcome outcome,
        Guid userId,
        CancellationToken cancellationToken)
    {
        if (userId == Guid.Empty)
        {
            return ValueTask.FromResult(
                AuditWriteResult.Rejected(
                    AuditRejectionCode.InvalidResource));
        }

        var resource = new AuditResourceRef(
            AuditResourceTypes.Account,
            userId.ToString("D"));

        var draft = new AuditEventDraft(
            eventType,
            outcome,
            Resource: resource);

        return auditWriter.WriteAsync(
            draft,
            cancellationToken);
    }
}
