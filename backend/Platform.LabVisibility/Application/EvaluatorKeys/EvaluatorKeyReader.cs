using Platform.Access.Contracts;
using Platform.Access.Domain.Enums;
using Platform.Access.Domain.Models;
using Platform.LabVisibility.Contracts;
using Platform.LabVisibility.Domain;

namespace Platform.LabVisibility.Application.EvaluatorKeys;

// Jedini put do evaluator ključa. Nijedan kontroler ga ne koristi (vidi EvaluatorKeyAccessTests).
public sealed class EvaluatorKeyReader(
    IAccessDecision access,
    IEvaluatorKeyStore keys,
    ILabVisibilityAudit audit)
{
    public async ValueTask<EvaluatorKey?> ReadAsync(
        SubjectRef? subject,
        Guid labId,
        string correlationId,
        CancellationToken cancellationToken = default)
    {
        var decision = await access.DecideAsync(
            new AccessRequest(
                subject,
                LabVisibilityActions.KeyRead,
                new ResourceRef("lab", labId.ToString()),
                correlationId),
            cancellationToken);

        if (decision.Outcome != AccessOutcome.Allow)
        {
            await WriteAuditAsync(subject, labId, "Deny", correlationId, cancellationToken);
            return null;
        }

        var key = await keys.GetAsync(labId, cancellationToken);
        await WriteAuditAsync(subject, labId, key is null ? "NotFound" : "Allow", correlationId, cancellationToken);
        return key;
    }

    // U audit ide samo ko, šta, nad kojom laboratorijom i ishod. Nikad sadržaj ključa (AUD-03).
    private ValueTask WriteAuditAsync(
        SubjectRef? subject,
        Guid labId,
        string outcome,
        string correlationId,
        CancellationToken cancellationToken) =>
        audit.WriteAsync(
            new LabVisibilityAuditEvent(
                LabVisibilityActions.KeyRead,
                labId,
                subject?.Id,
                outcome,
                correlationId),
            cancellationToken);
}
