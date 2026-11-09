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
            // Odluku je servis već doneo: prekinut zahtev ne sme da izgubi zapis, pa se token
            // pozivaoca namerno ne prosleđuje (isto kao kod ASN-04).
            return await writer.WriteAsync(ToDraft(entry), CancellationToken.None);
        }
        catch (Exception)
        {
            // Ugovor porta: false kada zapis nije sačuvan, nikad izuzetak. Izuzetak iz audita ne sme
            // da promeni ishod use-case-a (403 ostaje 403, 400 ostaje 400).
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
