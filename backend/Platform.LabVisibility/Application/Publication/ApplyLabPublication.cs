using Platform.LabVisibility.Contracts;
using Platform.LabVisibility.Domain;

namespace Platform.LabVisibility.Application.Publication;

public enum LabPublicationOutcome
{
    Published,
    Archived,
    Unchanged,
    NotFound,
    Rejected
}

// ReasonCode je samo kod (npr. "key_incomplete"), nikad sadržaj ni odgovor.
public sealed record LabPublicationResult(LabPublicationOutcome Outcome, string? ReasonCode = null);

// Nazivi radnji u auditu za promenu stanja (nisu radnje pristupa tima 3).
public static class LabPublicationAuditActions
{
    public const string Publish = "lab.publication.publish";
    public const string Archive = "lab.publication.archive";
}

// AUT-07 (KT2): primenjuje stanje objave koje javlja vlasnik objave (AUT-01/AUT-06, REV;
// ILabPublicationSource) na ono što student vidi kod nas.
//   nacrt → objavljena: samo ako ključ pokriva tačno sve zadatke laboratorije
//   objavljena → arhivirana
// Svaki drugi prelaz se odbija. Pokušaj objave i arhiviranja uvek ide u audit, bez sadržaja.
// Ključ se ne čita: IEvaluatorKeyCoverage vraća samo ID-jeve pokrivenih zadataka.
public sealed class ApplyLabPublication(
    ILabPublicationSource source,
    ILabPublicContentReader reader,
    IEvaluatorKeyCoverage keyCoverage,
    ILabPublicationStateStore states,
    ILabVisibilityAudit audit)
{
    public const string KeyIncomplete = "key_incomplete";
    public const string InvalidTransition = "invalid_transition";
    public const string StateChanged = "state_changed";

    public async ValueTask<LabPublicationResult> ExecuteAsync(
        Guid labId,
        string? actorId,
        string correlationId,
        CancellationToken cancellationToken = default)
    {
        var publication = await source.GetAsync(labId, cancellationToken);
        var content = await reader.GetAsync(labId, cancellationToken);

        // Nepoznata laboratorija kod vlasnika objave ili kod nas: ništa se ne menja.
        if (publication is null || content is null)
        {
            return new LabPublicationResult(LabPublicationOutcome.NotFound);
        }

        var target = ToState(publication.Status);
        if (target == content.State)
        {
            return new LabPublicationResult(LabPublicationOutcome.Unchanged);
        }

        return (content.State, target) switch
        {
            (LabVisibilityState.Draft, LabVisibilityState.Published) =>
                await PublishAsync(content, actorId, correlationId, cancellationToken),

            (LabVisibilityState.Published, LabVisibilityState.Archived) =>
                await ChangeAsync(
                    content,
                    LabVisibilityState.Archived,
                    LabPublicationAuditActions.Archive,
                    LabPublicationOutcome.Archived,
                    actorId,
                    correlationId,
                    cancellationToken),

            // Npr. arhivirana → objavljena, objavljena → nacrt, nacrt → arhivirana.
            _ => await RejectAsync(
                labId,
                target == LabVisibilityState.Archived
                    ? LabPublicationAuditActions.Archive
                    : LabPublicationAuditActions.Publish,
                InvalidTransition,
                actorId,
                correlationId,
                cancellationToken)
        };
    }

    private async ValueTask<LabPublicationResult> PublishAsync(
        LabPublicContent content,
        string? actorId,
        string correlationId,
        CancellationToken cancellationToken)
    {
        var covered = await keyCoverage.GetCoveredTaskIdsAsync(content.LabId, cancellationToken);
        var tasks = content.Tasks.Select(task => task.TaskId).ToHashSet(StringComparer.Ordinal);

        // Potpun ključ: tačno jedan odgovor za svaki zadatak, ni zadatak bez odgovora ni višak.
        if (tasks.Count == 0 || !tasks.SetEquals(covered))
        {
            return await RejectAsync(
                content.LabId,
                LabPublicationAuditActions.Publish,
                KeyIncomplete,
                actorId,
                correlationId,
                cancellationToken);
        }

        return await ChangeAsync(
            content,
            LabVisibilityState.Published,
            LabPublicationAuditActions.Publish,
            LabPublicationOutcome.Published,
            actorId,
            correlationId,
            cancellationToken);
    }

    private async ValueTask<LabPublicationResult> ChangeAsync(
        LabPublicContent content,
        LabVisibilityState next,
        string auditAction,
        LabPublicationOutcome outcome,
        string? actorId,
        string correlationId,
        CancellationToken cancellationToken)
    {
        // Uslovna promena: ako je neko u međuvremenu promenio stanje, ne prepisujemo ga.
        if (!await states.TryChangeStateAsync(content.LabId, content.State, next, cancellationToken))
        {
            return await RejectAsync(
                content.LabId,
                auditAction,
                StateChanged,
                actorId,
                correlationId,
                cancellationToken);
        }

        await WriteAuditAsync(content.LabId, auditAction, "success", actorId, correlationId, cancellationToken);
        return new LabPublicationResult(outcome);
    }

    private async ValueTask<LabPublicationResult> RejectAsync(
        Guid labId,
        string auditAction,
        string reasonCode,
        string? actorId,
        string correlationId,
        CancellationToken cancellationToken)
    {
        await WriteAuditAsync(labId, auditAction, reasonCode, actorId, correlationId, cancellationToken);
        return new LabPublicationResult(LabPublicationOutcome.Rejected, reasonCode);
    }

    private ValueTask WriteAuditAsync(
        Guid labId,
        string auditAction,
        string outcome,
        string? actorId,
        string correlationId,
        CancellationToken cancellationToken) =>
        audit.WriteAsync(
            new LabVisibilityAuditEvent(auditAction, labId, actorId, outcome, correlationId),
            cancellationToken);

    private static LabVisibilityState ToState(LabPublicationStatus status) => status switch
    {
        LabPublicationStatus.Draft => LabVisibilityState.Draft,
        LabPublicationStatus.Published => LabVisibilityState.Published,
        LabPublicationStatus.Archived => LabVisibilityState.Archived,
        _ => throw new ArgumentOutOfRangeException(nameof(status), status, null)
    };
}
