using Platform.AssignmentPrerequisites.Domain;

namespace Platform.AssignmentPrerequisites.Contracts;

public interface IAssignmentPrerequisiteStore
{
    ValueTask<IReadOnlyList<AssignmentPrerequisite>> GetForCourseAsync(
        string courseId,
        CancellationToken cancellationToken = default);

    ValueTask<IReadOnlyList<AssignmentPrerequisite>> GetForAssignmentAsync(
        Guid assignmentId,
        CancellationToken cancellationToken = default);

    ValueTask<bool> AddAsync(
        AssignmentPrerequisite prerequisite,
        CancellationToken cancellationToken = default);

    ValueTask<bool> RemoveAsync(
        Guid assignmentId,
        Guid requiredAssignmentId,
        CancellationToken cancellationToken = default);
}
