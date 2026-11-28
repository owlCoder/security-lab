using Platform.Prerequisites.Contracts;

namespace Platform.Prerequisites.Infrastructure;

public sealed class NoCompletedLaboratories : ICompletedLaboratories
{
    public Task<IReadOnlySet<Guid>> GetCompletedAsync(
        Guid studentId,
        CancellationToken cancellationToken = default)
    {
        IReadOnlySet<Guid> completed = new HashSet<Guid>();

        return Task.FromResult(completed);
    }
}