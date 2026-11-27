using Microsoft.EntityFrameworkCore;
using System.Data.Common;
using Laboratory = Platform.Api.Modules.Laboratories.Domain.Laboratory;
using Platform.Access.Domain.Enums;
using Platform.Access.Domain.Models;
using Platform.Api.Modules.Laboratories.Application;
using Platform.Laboratories.Domain.Models;
using Platform.Laboratories.Domain.Rules;
using Platform.Laboratories.Infrastructure.Persistence;

namespace Platform.Api.Modules.RevisionReviews;

public sealed class DraftRevisionService(
    IDbContextFactory<LaboratoriesDbContext> contexts,
    ILaboratoryRepository laboratories,
    Platform.Access.Contracts.IAccessDecision access,
    TimeProvider clock)
{
    public const string CreateAction = "revision.create";

    public async Task<IResult> CreateAsync(Guid labId, Guid courseId, Guid actor, SubjectRef subject,
        PublicationDefinition definition, string traceId, CancellationToken ct)
    {
        if (labId == Guid.Empty || courseId == Guid.Empty)
            return ReviewService.Error(400, "invalid_input");

        Laboratory? laboratory;
        try
        {
            laboratory = await laboratories.GetByIdAsync(labId, ct);
        }
        catch (DbException)
        {
            return ReviewService.Error(503, "laboratory_unavailable");
        }
        catch (TimeoutException)
        {
            return ReviewService.Error(503, "laboratory_unavailable");
        }
        if (laboratory is null || laboratory.CreatedBy != actor ||
            laboratory.CourseId == Guid.Empty || laboratory.CourseId != courseId)
            return ReviewService.Error(404, "not_found");

        var decision = await access.DecideAsync(new AccessRequest(subject, CreateAction,
            new ResourceRef("course", laboratory.CourseId.ToString("D")), traceId), ct);
        if (decision.Outcome != AccessOutcome.Allow)
            return ReviewService.Error(403, "forbidden");

        await using var db = await contexts.CreateDbContextAsync(ct);
        if (!await db.CoursePublishPolicies.AnyAsync(value => value.CourseId == laboratory.CourseId, ct))
            return ReviewService.Error(409, "policy_missing");

        var content = definition.Serialize();
        var hash = (RevisionContentHash.ComputationResult.Success)RevisionContentHash.Compute(content);
        var id = Guid.NewGuid();
        var revision = (LabRevision.CreationResult.Success)LabRevision.Create(id, laboratory.CourseId, actor, hash.Value);
        db.LabRevisions.Add(revision.Value);
        db.ReviewDefinitions.Add(new ReviewDefinition(id, labId, content, clock.GetUtcNow().ToUniversalTime()));
        await db.SaveChangesAsync(ct);

        return Results.Created($"/api/revisions/{id}", new { id, rowVersion = "1", contentHash = hash.Value });
    }
}

public sealed record CreateReviewDraft(Guid LaboratoryId, Guid CourseId, PublicationDefinition? Definition);
