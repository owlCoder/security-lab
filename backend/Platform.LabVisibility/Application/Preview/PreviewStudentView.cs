using Platform.Access.Contracts;
using Platform.Access.Domain.Enums;
using Platform.Access.Domain.Models;
using Platform.LabVisibility.Application.StudentView;
using Platform.LabVisibility.Contracts;

namespace Platform.LabVisibility.Application.Preview;

public enum PreviewOutcome
{
    Ok,
    Unauthenticated,
    NotFound
}

// Ono što ide klijentu: { isPreview, state, view }.
// View je isti tip kao studentski prikaz, pa pregled ne može da pokaže više od njega.
public sealed record LabPreview(bool IsPreview, string State, StudentLabView View);

public sealed record PreviewResult(PreviewOutcome Outcome, LabPreview? Preview);

// AUT-07: "Pregled kao student" za nastavnika kursa. Radi i za nacrt.
// Namerno ne zavisi od IEvaluatorKeyStore: ključ nikad ne ulazi u ovaj tok.
public sealed class PreviewStudentView(
    IAccessDecision accessDecision,
    ILabPublicContentReader reader,
    ILabVisibilityAudit audit)
{
    public async ValueTask<PreviewResult> ExecuteAsync(
        Guid labId,
        SubjectRef? caller,
        string correlationId,
        CancellationToken cancellationToken = default)
    {
        var decision = await accessDecision.DecideAsync(
            new AccessRequest(
                caller,
                LabVisibilityActions.Preview,
                new ResourceRef("lab", labId.ToString()),
                correlationId),
            cancellationToken);

        if (decision.Outcome != AccessOutcome.Allow)
        {
            await WriteAuditAsync(labId, caller, "denied", correlationId, cancellationToken);

            // Bez identiteta je 401; sve ostalo (zabranjeno ili nepostojeće) izgleda isto.
            return decision.DenialReason == AccessDenialReason.Unauthenticated
                ? new PreviewResult(PreviewOutcome.Unauthenticated, null)
                : new PreviewResult(PreviewOutcome.NotFound, null);
        }

        // Za razliku od studentskog prikaza, ovde NE proveravamo da je laboratorija objavljena.
        var content = await reader.GetAsync(labId, cancellationToken);
        if (content is null)
        {
            await WriteAuditAsync(labId, caller, "not_found", correlationId, cancellationToken);
            return new PreviewResult(PreviewOutcome.NotFound, null);
        }

        var view = StudentViewProjector.Project(content);
        await WriteAuditAsync(labId, caller, "allowed", correlationId, cancellationToken);

        return new PreviewResult(
            PreviewOutcome.Ok,
            new LabPreview(true, content.State.ToString(), view));
    }

    private ValueTask WriteAuditAsync(
        Guid labId,
        SubjectRef? caller,
        string outcome,
        string correlationId,
        CancellationToken cancellationToken) =>
        audit.WriteAsync(
            new LabVisibilityAuditEvent(
                LabVisibilityActions.Preview,
                labId,
                caller?.Id,
                outcome,
                correlationId),
            cancellationToken);
}
