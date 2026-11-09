using Microsoft.EntityFrameworkCore;
using Platform.LabVisibility.Contracts;
using Platform.LabVisibility.Domain;

namespace Platform.LabVisibility.Infrastructure.Persistence;

// Čita SAMO lab_public_content i lab_public_task. Tabelu lab_evaluator_key nikad ne dira.
// Singleton: za svaki poziv otvara nov DbContext iz fabrike (DbContext nije thread-safe).
public sealed class EfLabPublicContentReader(IDbContextFactory<LabVisibilityDbContext> dbFactory) : ILabPublicContentReader
{
    public async ValueTask<LabPublicContent?> GetAsync(Guid labId, CancellationToken cancellationToken = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);

        var row = await db.PublicContents
            .AsNoTracking()
            .Include(x => x.Tasks)
            .SingleOrDefaultAsync(x => x.LabId == labId, cancellationToken);

        return row is null ? null : LabVisibilityMapping.ToDomain(row);
    }
}
