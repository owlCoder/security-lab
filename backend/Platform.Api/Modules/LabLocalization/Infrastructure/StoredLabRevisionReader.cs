using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Platform.Api.Modules.RevisionReviews;
using Platform.LabLocalization.Application;
using Platform.Laboratories.Domain.Enums;
using Platform.Laboratories.Infrastructure.Persistence;

namespace Platform.Api.Modules.LabLocalization.Infrastructure;

// Čita revizije iz REV modula: objavljena revizija ima broj i nepromenljiv snapshot definicije sa evaluatorom.
// Srpski tekst je tekst same revizije: Title -> title, Goal -> description, Question -> instructions.
public sealed class StoredLabRevisionReader(IDbContextFactory<LaboratoriesDbContext> contexts) : ILabRevisionReader
{
    public async Task<RevisionInfo?> FindCurrentForLabAsync(Guid labId, CancellationToken ct = default)
    {
        if (labId == Guid.Empty)
        {
            return null;
        }

        await using var db = await contexts.CreateDbContextAsync(ct);
        var current = await (
                from publication in db.ReviewOperations.AsNoTracking()
                join revision in db.LabRevisions.AsNoTracking() on publication.RevisionId equals revision.Id
                where publication.LaboratoryId == labId
                      && publication.Number != null
                      && publication.Snapshot != null
                      && revision.Status == LabRevisionStatus.Published
                orderby publication.Number descending
                select new { publication.RevisionId, publication.Snapshot })
            .FirstOrDefaultAsync(ct);

        if (current is null ||
            Read(current.Snapshot!) is not { Title: { Length: > 0 } title, Goal: { } goal, Question: { } question } definition ||
            EvaluatorVersionOf(definition) is not { } evaluator)
        {
            return null;
        }

        return new RevisionInfo(current.RevisionId, evaluator, new RevisionText(title, goal, question), IsDraft: false);
    }

    public async Task<RevisionInfo?> FindForLabAsync(Guid labId, Guid revisionId, CancellationToken ct = default)
    {
        if (labId == Guid.Empty || revisionId == Guid.Empty)
        {
            return null;
        }

        await using var db = await contexts.CreateDbContextAsync(ct);
        var found = await (
                from definition in db.ReviewDefinitions.AsNoTracking()
                join revision in db.LabRevisions.AsNoTracking() on definition.RevisionId equals revision.Id
                where definition.RevisionId == revisionId && definition.LaboratoryId == labId
                select new { definition.Content, revision.Status })
            .SingleOrDefaultAsync(ct);

        if (found is null)
        {
            return null;
        }

        // Nacrt još ne mora imati ispravan evaluator ni ceo tekst; za dodavanje prevoda dovoljni su ID i stanje.
        var content = Read(found.Content);
        return new RevisionInfo(
            revisionId,
            content is null ? string.Empty : EvaluatorVersionOf(content) ?? string.Empty,
            new RevisionText(content?.Title ?? string.Empty, content?.Goal ?? string.Empty, content?.Question ?? string.Empty),
            IsDraft: found.Status == LabRevisionStatus.Draft);
    }

    private static PublicationDefinition? Read(byte[] definition)
    {
        try
        {
            return PublicationDefinition.Read(definition);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static string? EvaluatorVersionOf(PublicationDefinition definition) =>
        definition.Evaluator is { Kind: { Length: > 0 } kind, Version: > 0 } evaluator
            ? $"{kind}-v{evaluator.Version}"
            : null;
}
