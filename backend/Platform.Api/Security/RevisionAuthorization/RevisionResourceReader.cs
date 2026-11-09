using Microsoft.EntityFrameworkCore;
using Platform.Laboratories.Infrastructure.Persistence;

namespace Platform.Api.Security.RevisionAuthorization;

public sealed class RevisionResourceReader(IDbContextFactory<LaboratoriesDbContext> contexts)
    : IRevisionResourceReader
{
    public async Task<RevisionResource?> FindAsync(Guid revisionId, CancellationToken cancellationToken = default)
    {
        if (revisionId == Guid.Empty)
            return null;

        await using var db = await contexts.CreateDbContextAsync(cancellationToken);
        return await (from revision in db.LabRevisions.AsNoTracking()
            where revision.Id == revisionId
            join policy in db.CoursePublishPolicies on revision.CourseId equals policy.CourseId into policies
            from policy in policies.DefaultIfEmpty()
            select new RevisionResource(revision.CourseId, revision.AuthorId,
                policy == null ? null : policy.RequiresIndependentApproval))
            .SingleOrDefaultAsync(cancellationToken);
    }
}
