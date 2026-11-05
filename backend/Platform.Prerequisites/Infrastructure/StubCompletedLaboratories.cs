using Platform.Prerequisites.Contracts;

namespace Platform.Prerequisites.Infrastructure;

public sealed class StubCompletedLaboratories : ICompletedLaboratories
{
    private readonly Dictionary<Guid, HashSet<Guid>> _completed = new();

    public void MarkCompleted(Guid studentId, Guid laboratoryId)
    {
        if (!_completed.TryGetValue(studentId, out HashSet<Guid>? laboratories))
        {
            laboratories = new HashSet<Guid>();
            _completed[studentId] = laboratories;
        }

        laboratories.Add(laboratoryId);
    }

    public Task<IReadOnlySet<Guid>> GetCompletedAsync(
        Guid studentId,
        CancellationToken cancellationToken = default)
    {
        IReadOnlySet<Guid> result = _completed.TryGetValue(studentId, out HashSet<Guid>? laboratories)
            ? new HashSet<Guid>(laboratories)
            : new HashSet<Guid>();

        return Task.FromResult(result);
    }
}