using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Storage;

namespace Team21.Acc.Infrastructure.Common;

/// <summary>
/// What every one of our context interfaces has. Services inject ICoursesDbContext or
/// IGroupsDbContext, never the concrete context.
/// </summary>
public interface ITeamDbContext
{
    ChangeTracker ChangeTracker { get; }

    void ClearChangeTracker();

    Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);

    Task<IDbContextTransaction> BeginTransactionAsync(CancellationToken cancellationToken = default);
}
