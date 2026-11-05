using Platform.Access.Domain.Models;

namespace Platform.Access.Contracts;

public interface IMembershipReader
{
    ValueTask<CourseMembership?> GetAsync(
        Guid accountId,
        Guid courseId,
        CancellationToken cancellationToken = default);
}
