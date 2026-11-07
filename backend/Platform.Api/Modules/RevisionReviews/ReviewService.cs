using System.Globalization;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Platform.Access.Contracts;
using Platform.Access.Domain.Enums;
using Platform.Access.Domain.Models;
using Platform.Laboratories.Domain.Enums;
using Platform.Laboratories.Domain.Models;
using Platform.Laboratories.Domain.Rules;
using Platform.Laboratories.Infrastructure.Persistence;

namespace Platform.Api.Modules.RevisionReviews;

public sealed class ReviewService(
    IDbContextFactory<LaboratoriesDbContext> contexts,
    IAccessDecision access,
    IPublicationSource publication,
    TimeProvider clock)
{
    public const string ReadAction = "revision.read";
    public const string PendingAction = "revision.pending.reviews";
    public const string MineAction = "revision.mine";

    public async Task<IResult> ApplyAsync(Guid id, Guid actor, SubjectRef subject, ReviewCommand command,
        string traceId, CancellationToken ct)
    {
        if (!await Allows(subject, "revision." + command.Action, id, traceId, ct) ||
            !await Allows(subject, ReadAction, id, traceId, ct))
            return Error(403, "forbidden");

        await using var db = await contexts.CreateDbContextAsync(ct);
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        // Serialize decisions and retries on the same revision before inspecting the receipt.
        var revision = await db.LabRevisions.FromSqlInterpolated($"""
            SELECT * FROM laboratories."LabRevisions" WHERE "Id" = {id} FOR UPDATE
            """).SingleOrDefaultAsync(ct);
        if (revision is null)
            return Error(404, "not_found");
        if (command.Action == "submit" && revision.AuthorId != actor)
            return Error(403, "forbidden");

        var prior = await db.ReviewOperations.FindAsync([id, actor, command.Key], ct);
        if (prior is not null)
        {
            if (!Matches(prior, command))
                return Error(409, "idempotency_key_reused");

            return Results.Ok(await Detail(db, revision, subject, traceId, ct, prior));
        }

        if (revision.RowVersion != command.Version || revision.ContentHash != command.Hash)
            return Error(409, "revision_changed");

        var policy = await db.CoursePublishPolicies.FromSqlInterpolated($"""
            SELECT * FROM laboratories."CoursePublishPolicies"
            WHERE "CourseId" = {revision.CourseId} FOR SHARE
            """).SingleOrDefaultAsync(ct);
        if (policy is null)
            return Error(409, "policy_missing");

        var decision = command.Action == "approve"
            ? RevisionReviewDecision.Approved : RevisionReviewDecision.ReturnedForChanges;
        var outcome = command.Action == "submit"
            ? revision.SubmitForReview(command.Version)
            : revision.Decide(actor, command.Version, command.Hash, decision, policy);
        if (outcome != RevisionChangeOutcome.Applied)
            return outcome == RevisionChangeOutcome.SelfApprovalForbidden
                ? Error(403, "self_approval_forbidden") : Error(409, "invalid_state");

        PublicationCandidate? candidate = null;
        byte[]? snapshot = null;
        int? number = null;
        if (command.Action == "approve")
        {
            candidate = await publication.LoadAndValidateAsync(id, ct);
            if (candidate is null || candidate.LaboratoryId == Guid.Empty)
                return Error(503, "publication_unavailable");
            if (candidate.Errors.Count > 0)
                return Results.Json(new { code = "validation_failed", errors = candidate.Errors }, statusCode: 422);

            snapshot = candidate.Snapshot.ToArray();
            if (RevisionContentHash.Compute(snapshot) is not RevisionContentHash.ComputationResult.Success hash ||
                hash.Value != command.Hash)
                return Error(409, "revision_changed");

            // Two revisions of the same laboratory can be approved concurrently.
            var scope = "rev05:" + candidate.LaboratoryId.ToString("D");
            await db.Database.ExecuteSqlInterpolatedAsync(
                $"SELECT pg_advisory_xact_lock(hashtextextended({scope}, 0))", ct);
            var lastNumber = await db.ReviewOperations.Where(value => value.LaboratoryId == candidate.LaboratoryId)
                .MaxAsync(value => value.Number, ct) ?? 0;
            if (lastNumber == int.MaxValue)
                return Error(409, "revision_number_exhausted");
            number = lastNumber + 1;
        }

        var now = clock.GetUtcNow().ToUniversalTime();
        var operation = new ReviewOperation
        {
            Id = Guid.NewGuid(), RevisionId = id, ActorId = actor, Key = command.Key, Action = command.Action,
            Hash = command.Hash, Version = command.Version, Comment = command.Comment, At = now,
            LaboratoryId = candidate?.LaboratoryId, Number = number, Snapshot = snapshot
        };
        if (command.Action != "submit")
        {
            var record = RevisionDecisionRecord.Create(operation.Id, id, actor, decision,
                now, command.Hash, policy.RequiresIndependentApproval);
            if (record is not RevisionDecisionRecord.CreationResult.Success created)
                return Error(500, "storage_failure");
            db.RevisionDecisions.Add(created.Value);
        }
        db.ReviewOperations.Add(operation);
        try
        {
            await db.SaveChangesAsync(ct);
            await transaction.CommitAsync(ct);
        }
        catch (DbUpdateConcurrencyException)
        {
            return Error(409, "revision_changed");
        }
        catch (DbUpdateException)
        {
            return Error(500, "storage_failure");
        }

        return Results.Ok(await Detail(db, revision, subject, traceId, ct, operation));
    }

    public async Task<IResult> GetAsync(Guid id, SubjectRef subject, string traceId, CancellationToken ct)
    {
        if (!await Allows(subject, ReadAction, id, traceId, ct))
            return Error(404, "not_found");
        await using var db = await contexts.CreateDbContextAsync(ct);
        var revision = await db.LabRevisions.AsNoTracking().SingleOrDefaultAsync(value => value.Id == id, ct);
        return revision is null ? Error(404, "not_found") :
            Results.Ok(await Detail(db, revision, subject, traceId, ct));
    }

    public async Task<IResult> ListAsync(Guid? course, Guid actor, SubjectRef subject, bool pending,
        string traceId, CancellationToken ct)
    {
        await using var db = await contexts.CreateDbContextAsync(ct);
        var query = db.LabRevisions.AsNoTracking();
        query = pending ? query.Where(value => value.Status == LabRevisionStatus.InReview)
            : query.Where(value => value.AuthorId == actor);
        if (course is Guid courseId)
            query = query.Where(value => value.CourseId == courseId);

        var result = new List<ReviewSummary>();
        foreach (var revision in await query.OrderBy(value => value.Id).ToListAsync(ct))
        {
            if (await Allows(subject, pending ? PendingAction : MineAction, revision.Id, traceId, ct))
                result.Add(Summary(revision, await History(db, revision.Id, revision.RowVersion - 1, ct)));
        }
        return Results.Ok(result);
    }

    private async Task<ReviewDetail> Detail(LaboratoriesDbContext db, LabRevision revision, SubjectRef subject,
        string traceId, CancellationToken ct, ReviewOperation? receipt = null)
    {
        var version = receipt is null ? revision.RowVersion : receipt.Version + 1;
        var history = await History(db, revision.Id, version - 1, ct);
        var policy = await db.CoursePublishPolicies.FindAsync([revision.CourseId], ct);
        var status = receipt?.Action switch
        {
            "submit" => LabRevisionStatus.InReview,
            "return" => LabRevisionStatus.Draft,
            "approve" => LabRevisionStatus.Published,
            _ => revision.Status
        };
        var author = Guid.TryParse(subject.Id, out var actor) && actor == revision.AuthorId;
        var actions = new List<string>();
        if (policy is not null)
        {
            if (status == LabRevisionStatus.Draft && author &&
                await Allows(subject, "revision.submit", revision.Id, traceId, ct))
                actions.Add("submit");
            if (status == LabRevisionStatus.InReview)
            {
                if ((!author || !policy.RequiresIndependentApproval) &&
                    await Allows(subject, "revision.approve", revision.Id, traceId, ct))
                    actions.Add("approve");
                if (await Allows(subject, "revision.return", revision.Id, traceId, ct))
                    actions.Add("return");
            }
        }
        var summary = Summary(revision, history, status);
        var definition = await db.ReviewDefinitions.AsNoTracking()
            .SingleOrDefaultAsync(value => value.RevisionId == revision.Id, ct);
        ReviewPreview? preview = null;
        if (definition is not null)
        {
            summary = summary with { LabId = definition.LaboratoryId, UpdatedAt = summary.UpdatedAt ?? definition.CreatedAt };
            try
            {
                if (PublicationDefinition.Read(definition.Content) is { } content)
                {
                    summary = summary with { LabTitle = content.Title ?? summary.LabTitle };
                    preview = new ReviewPreview(content.Type, content.Goal, content.Events, content.Question);
                }
            }
            catch (JsonException)
            {
                // The validator rejects corrupt stored content. Do not expose raw bytes.
            }
        }
        return new ReviewDetail(summary, receipt?.Hash ?? revision.ContentHash,
            version.ToString(CultureInfo.InvariantCulture), new ReviewPolicy(policy?.RequiresIndependentApproval ?? true),
            actions, history.Select(entry => new ReviewDecision(entry.Id, DecisionType(entry.Action),
                Person(entry.ActorId), entry.At, entry.Comment, entry.Hash, entry.Number)).ToArray(), preview);
    }

    private static Task<List<HistoryEntry>> History(LaboratoriesDbContext db, Guid id, long maxVersion, CancellationToken ct) =>
        db.ReviewOperations.Where(entry => entry.RevisionId == id && entry.Version <= maxVersion)
            .OrderBy(entry => entry.Version).Select(entry => new HistoryEntry(entry.Id, entry.ActorId,
                entry.Action, entry.At, entry.Comment, entry.Hash, entry.Number, entry.LaboratoryId)).ToListAsync(ct);

    private static ReviewSummary Summary(LabRevision revision, List<HistoryEntry> history, LabRevisionStatus? status = null)
    {
        var last = history.LastOrDefault();
        var published = history.LastOrDefault(entry => entry.Number.HasValue);
        return new ReviewSummary(revision.Id, published?.LaboratoryId, "Revizija " + revision.Id,
            revision.CourseId, revision.CourseId.ToString(), (status ?? revision.Status).ToString(),
            published?.Number, Person(revision.AuthorId), history.LastOrDefault(entry => entry.Action == "submit")?.At, last?.At);
    }

    private async Task<bool> Allows(SubjectRef subject, string action, Guid id, string traceId, CancellationToken ct) =>
        (await access.DecideAsync(new AccessRequest(subject, action, new ResourceRef("revision", id.ToString("D")),
            traceId), ct)).Outcome == AccessOutcome.Allow;

    public static bool Matches(ReviewOperation prior, ReviewCommand command) =>
        prior.Action == command.Action && prior.Version == command.Version && prior.Hash == command.Hash &&
        prior.Comment == command.Comment;

    public static IResult Error(int status, string code) => Results.Json(new { code }, statusCode: status);

    private static ReviewPerson Person(Guid id) => new(id, id.ToString());
    private static string DecisionType(string action) => action switch
    {
        "submit" => "Submitted",
        "approve" => "Approved",
        _ => "Returned"
    };

    private sealed record HistoryEntry(Guid Id, Guid ActorId, string Action, DateTimeOffset At, string? Comment,
        string Hash, int? Number, Guid? LaboratoryId);
}
