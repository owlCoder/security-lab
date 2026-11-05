namespace Platform.Assignments.Contracts;

// ASN-06 TIM 15
// Records refused or failed extensions. A successful extension is recorded by
// IDeadlineExtensionStore.SaveAsync together with the change itself.
// Returns false when the record could not be stored. A failed audit write never grants access.
public interface IDeadlineExtensionAuditWriter
{
    ValueTask<bool> TryWriteAsync(
        DeadlineExtensionAuditEntry entry,
        CancellationToken cancellationToken = default);
}
