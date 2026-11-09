using Microsoft.EntityFrameworkCore;
using Platform.Laboratories.Infrastructure.Persistence;

namespace Platform.Api.Security.RevisionAuthorization;

public sealed class RevisionAuthorReader(
    IDbContextFactory<LaboratoriesDbContext> contexts) : IRevisionAuthorReader
{
    public async Task<Guid?> FindAuthorIdAsync(
        Guid revisionId,
        CancellationToken cancellationToken = default)
    {
        if (revisionId == Guid.Empty)
        {
            return null;
        }

        await using var context = await contexts.CreateDbContextAsync(cancellationToken);

        return await context.LabRevisions
            .AsNoTracking()
            .Where(revision => revision.Id == revisionId)
            .Select(revision => (Guid?)revision.AuthorId)
            .SingleOrDefaultAsync(cancellationToken);
    }
}
