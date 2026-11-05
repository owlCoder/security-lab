using Platform.Assignments.Domain.Models;

namespace Platform.Assignments.Contracts;

// ASN-06 TIM 15
public interface IDeadlineExtensionStore
{
    ValueTask<StudentDeadlineOverride?> GetAsync(
        Guid assignmentId,
        string studentId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Stores the individual deadline and its audit record as one unit: both or neither
    /// (one transaction, or the change plus an outbox entry; Audit decision D3).
    /// It must also throw <see cref="InvalidOperationException"/> when the deadline that is stored now
    /// is not <see cref="StudentDeadlineOverride.PreviousDueAtUtc"/>, so a concurrent change is never
    /// overwritten silently. It must throw, never return, when it cannot store both.
    /// </summary>
    ValueTask SaveAsync(
        StudentDeadlineOverride deadlineOverride,
        DeadlineExtensionAuditEntry auditEntry,
        CancellationToken cancellationToken = default);
}
