using Platform.Access.Contracts;
using Platform.Access.Domain.Models;

namespace Platform.Access.Infrastructure.Membership;

public sealed class EmptyMembershipReader : IMembershipReader
{
    public ValueTask<CourseMembership?> GetAsync(
        Guid accountId,
        Guid courseId,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return ValueTask.FromResult<CourseMembership?>(null);
    }
}
