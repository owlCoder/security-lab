using Platform.AssignmentPrerequisites.Contracts;
using Platform.AssignmentPrerequisites.Domain;

namespace Platform.AssignmentPrerequisites.Infrastructure;

public sealed class InMemoryAssignmentPrerequisiteStore : IAssignmentPrerequisiteStore
{
    private readonly List<AssignmentPrerequisite> _rows = [];
    private readonly Lock _gate = new();

    public ValueTask<IReadOnlyList<AssignmentPrerequisite>> GetForCourseAsync(
        string courseId,
        CancellationToken cancellationToken = default)
    {
        lock (_gate)
        {
            IReadOnlyList<AssignmentPrerequisite> result = _rows
                .Where(p => string.Equals(p.CourseId, courseId, StringComparison.Ordinal))
                .ToList();

            return ValueTask.FromResult(result);
        }
    }

    public ValueTask<IReadOnlyList<AssignmentPrerequisite>> GetForAssignmentAsync(
        Guid assignmentId,
        CancellationToken cancellationToken = default)
    {
        lock (_gate)
        {
            IReadOnlyList<AssignmentPrerequisite> result = _rows
                .Where(p => p.AssignmentId == assignmentId)
                .ToList();

            return ValueTask.FromResult(result);
        }
    }

    public ValueTask<bool> AddAsync(
        AssignmentPrerequisite prerequisite,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(prerequisite);

        lock (_gate)
        {
            if (_rows.Any(prerequisite.IsSameRelation))
            {
                return ValueTask.FromResult(false);
            }

            _rows.Add(prerequisite);
            return ValueTask.FromResult(true);
        }
    }

    public ValueTask<bool> RemoveAsync(
        Guid assignmentId,
        Guid requiredAssignmentId,
        CancellationToken cancellationToken = default)
    {
        lock (_gate)
        {
            int removed = _rows.RemoveAll(p =>
                p.AssignmentId == assignmentId &&
                p.RequiredAssignmentId == requiredAssignmentId);

            return ValueTask.FromResult(removed > 0);
        }
    }
}
