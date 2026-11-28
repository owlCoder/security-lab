using Microsoft.EntityFrameworkCore;
using Platform.LabVisibility.Contracts;

namespace Platform.LabVisibility.Infrastructure.Persistence;

// AUT-07 (KT2): koje zadatke ključ pokriva, za proveru pre objave.
// Upit bira SAMO kolonu task_id iz lab_evaluator_key; kolona sa odgovorom se nikad ne čita.
public sealed class EfEvaluatorKeyCoverage(IDbContextFactory<LabVisibilityDbContext> dbFactory) : IEvaluatorKeyCoverage
{
    public async ValueTask<IReadOnlySet<string>> GetCoveredTaskIdsAsync(
        Guid labId,
        CancellationToken cancellationToken = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);

        var taskIds = await db.EvaluatorKeys
            .AsNoTracking()
            .Where(x => x.LabId == labId)
            .Select(x => x.TaskId)
            .ToListAsync(cancellationToken);

        return taskIds.ToHashSet(StringComparer.Ordinal);
    }
}
