using Microsoft.EntityFrameworkCore;
using Npgsql;
using Platform.Authoring.Application;
using Platform.Authoring.Domain;

namespace Platform.Authoring.Infrastructure.Persistence;

/// <summary>
/// PostgreSQL implementation of <see cref="IDraftRepository"/> (AUT-01, AUT-06). Same contract as
/// <see cref="InMemoryDraftStore"/>: duplicate ids and stale saves throw CONFLICT, a missing draft on save
/// throws NOT_FOUND. The stale-save check is one conditional UPDATE, so two concurrent edits cannot both win.
/// </summary>
internal sealed class PostgresDraftRepository(AuthoringDbContext db) : IDraftRepository
{
    private const string UniqueViolation = "23505";

    public async Task<LabDraft?> GetAsync(Guid id, CancellationToken ct = default)
    {
        var row = await db.Drafts.AsNoTracking().FirstOrDefaultAsync(d => d.Id == id, ct);
        return row is null ? null : ToDomain(row);
    }

    public async Task AddAsync(LabDraft draft, CancellationToken ct = default)
    {
        db.Drafts.Add(ToRow(draft));
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException { SqlState: UniqueViolation })
        {
            db.ChangeTracker.Clear();
            throw new DomainRuleException(AuthoringErrorCodes.Conflict);
        }
        finally
        {
            // Nothing is read back through this context, so do not keep the row tracked.
            db.ChangeTracker.Clear();
        }
    }

    public async Task SaveAsync(LabDraft draft, int previousVersion, CancellationToken ct = default)
    {
        var updated = await db.Drafts
            .Where(d => d.Id == draft.Id && d.Version == previousVersion)
            .ExecuteUpdateAsync(set => set
                .SetProperty(d => d.Status, (int)draft.Status)
                .SetProperty(d => d.Title, draft.Content.Title)
                .SetProperty(d => d.Goal, draft.Content.Goal)
                .SetProperty(d => d.LabType, (int)draft.Content.Type)
                .SetProperty(d => d.Version, draft.Version), ct);

        if (updated == 1) return;

        var exists = await db.Drafts.AnyAsync(d => d.Id == draft.Id, ct);
        throw new DomainRuleException(exists ? AuthoringErrorCodes.Conflict : AuthoringErrorCodes.NotFound);
    }

    private static DraftRow ToRow(LabDraft d) => new()
    {
        Id = d.Id,
        OwnerId = d.OwnerId,
        SourceTemplateId = d.SourceTemplateId,
        Status = (int)d.Status,
        Title = d.Content.Title,
        Goal = d.Content.Goal,
        LabType = (int)d.Content.Type,
        Version = d.Version,
    };

    // Goes through the domain factories, so a corrupted row cannot produce an invalid draft.
    private static LabDraft ToDomain(DraftRow r) => LabDraft.Rehydrate(
        r.Id, r.OwnerId, r.SourceTemplateId, (DraftStatus)r.Status,
        LabContent.Create(r.Title, r.Goal, (LabType)r.LabType), r.Version);
}
