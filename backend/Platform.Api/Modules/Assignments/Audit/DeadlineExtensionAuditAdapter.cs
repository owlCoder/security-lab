using Platform.Assignments.Contracts;

namespace Platform.Api.Modules.Assignments.Audit;

// ASN-06 TIM 15 (celina 5)

/// <summary>
/// Implementacija porta <see cref="IDeadlineExtensionAuditWriter"/> iz Platform.Assignments.
/// <c>ExtendDeadlineService</c> (član 3) je poziva za odbijene (Denied) i neuspele (Failure) pokušaje;
/// uspeh upisuje <c>IDeadlineExtensionStore.SaveAsync</c> (član 1) u istoj transakciji, a za to može
/// da koristi <see cref="ToDraft"/> i <see cref="AssignmentAuditDraftMapper.ToPlatformDraft"/>.
/// </summary>
public sealed class DeadlineExtensionAuditAdapter(IAssignmentAuditWriter writer) : IDeadlineExtensionAuditWriter
{
    public async ValueTask<bool> TryWriteAsync(
        DeadlineExtensionAuditEntry entry,
        CancellationToken cancellationToken = default)
    {
        if (entry is null)
        {
            return false;
        }

        try
        {
            return await writer.WriteAsync(ToDraft(entry), cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception)
        {
            // Ugovor: false kada zapis nije sačuvan. Alarm je već podigao AssignmentAuditWriter.
            return false;
        }
    }

    public static AssignmentAuditDraft ToDraft(DeadlineExtensionAuditEntry entry)
    {
        ArgumentNullException.ThrowIfNull(entry);

        return new AssignmentAuditDraft(
            AssignmentAuditActions.ExtendDeadline,
            ToOutcome(entry.Outcome),
            entry.AssignmentId,
            entry.CourseId,
            entry.StudentId,
            entry.ReasonCode,
            entry.PreviousDueAtUtc,
            entry.NewDueAtUtc);
    }

    public static AssignmentAuditOutcome ToOutcome(DeadlineExtensionAuditOutcome outcome) => outcome switch
    {
        DeadlineExtensionAuditOutcome.Success => AssignmentAuditOutcome.Success,
        DeadlineExtensionAuditOutcome.Denied => AssignmentAuditOutcome.Denied,
        DeadlineExtensionAuditOutcome.Failure => AssignmentAuditOutcome.Failure,
        _ => throw new ArgumentOutOfRangeException(nameof(outcome))
    };
}
