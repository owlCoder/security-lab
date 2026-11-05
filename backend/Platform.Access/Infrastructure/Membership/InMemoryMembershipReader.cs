using System.Collections.Concurrent;
using System.Collections.Frozen;
using Platform.Access.Contracts;
using Platform.Access.Domain.Models;

namespace Platform.Access.Infrastructure.Membership;

public sealed class InMemoryMembershipReader : IMembershipReader
{
    private readonly ConcurrentDictionary<(Guid AccountId, Guid CourseId), CourseMembership> memberships = new();

    public void Set(CourseMembership membership)
    {
        ArgumentNullException.ThrowIfNull(membership);
        var snapshot = membership with
        {
            DelegatedActions = membership.DelegatedActions is null
                ? new HashSet<string>(StringComparer.Ordinal).ToFrozenSet()
                : membership.DelegatedActions.ToFrozenSet(StringComparer.Ordinal)
        };
        memberships[(membership.AccountId, membership.CourseId)] = snapshot;
    }

    public bool Remove(Guid accountId, Guid courseId) =>
        memberships.TryRemove((accountId, courseId), out _);

    public ValueTask<CourseMembership?> GetAsync(
        Guid accountId,
        Guid courseId,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        memberships.TryGetValue((accountId, courseId), out var membership);
        return ValueTask.FromResult(membership);
    }
}
