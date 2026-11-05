using Microsoft.EntityFrameworkCore;
using Platform.LabVisibility.Contracts;
using Platform.LabVisibility.Domain;

namespace Platform.LabVisibility.Infrastructure.Persistence;

// Jedina klasa koja čita lab_evaluator_key. Koristi je samo EvaluatorKeyReader (Kovačević).
// Singleton: za svaki poziv otvara nov DbContext iz fabrike (DbContext nije thread-safe).
public sealed class EfEvaluatorKeyStore(IDbContextFactory<LabVisibilityDbContext> dbFactory) : IEvaluatorKeyStore
{
    public async ValueTask<EvaluatorKey?> GetAsync(Guid labId, CancellationToken cancellationToken = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);

        // EvaluatorKey.Create traži javni sadržaj da proveri da svaki zadatak ima tačno jedan odgovor.
        var contentRow = await db.PublicContents
            .AsNoTracking()
            .Include(x => x.Tasks)
            .SingleOrDefaultAsync(x => x.LabId == labId, cancellationToken);

        if (contentRow is null)
            return null;

        var keyRows = await db.EvaluatorKeys
            .AsNoTracking()
            .Where(x => x.LabId == labId)
            .ToListAsync(cancellationToken);

        if (keyRows.Count == 0)
            return null;

        var content = LabVisibilityMapping.ToDomain(contentRow);
        var answers = keyRows
            .Select(r => new TaskAnswerKey(r.TaskId, r.ExpectedAnswer))
            .ToArray();

        // Ako baza ima nepotpun ključ, Create baca izuzetak: radije greška nego pogrešna ocena.
        return EvaluatorKey.Create(content, answers);
    }

    public async ValueTask SaveAsync(EvaluatorKey key, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(key);

        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);

        // Stari ključ se menja celim novim, u jednoj transakciji.
        await using var tx = await db.Database.BeginTransactionAsync(cancellationToken);

        await db.EvaluatorKeys
            .Where(x => x.LabId == key.LabId)
            .ExecuteDeleteAsync(cancellationToken);

        db.EvaluatorKeys.AddRange(LabVisibilityMapping.ToRows(key));
        await db.SaveChangesAsync(cancellationToken);

        await tx.CommitAsync(cancellationToken);
    }
}
