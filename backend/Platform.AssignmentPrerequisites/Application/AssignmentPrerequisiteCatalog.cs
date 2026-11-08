using Platform.AssignmentPrerequisites.Contracts;
using Platform.AssignmentPrerequisites.Domain;
using Platform.Assignments.Contracts;
using Platform.Assignments.Domain.Models;

namespace Platform.AssignmentPrerequisites.Application;

public sealed class AssignmentPrerequisiteCatalog
{
    private readonly IAssignmentPrerequisiteStore _store;
    private readonly IAssignmentRepository _assignments;

    public AssignmentPrerequisiteCatalog(
        IAssignmentPrerequisiteStore store,
        IAssignmentRepository assignments)
    {
        _store = store;
        _assignments = assignments;
    }

    public async ValueTask<AddPrerequisiteOutcome> AddAsync(
        AssignmentPrerequisite candidate,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(candidate);

        if (!await BelongsToCourseAsync(candidate.AssignmentId, candidate.CourseId, cancellationToken) ||
            !await BelongsToCourseAsync(candidate.RequiredAssignmentId, candidate.CourseId, cancellationToken))
        {
            return AddPrerequisiteOutcome.AssignmentNotInCourse;
        }

        IReadOnlyList<AssignmentPrerequisite> existing =
            await _store.GetForCourseAsync(candidate.CourseId, cancellationToken);

        if (PrerequisiteGraph.IsDuplicate(existing, candidate))
        {
            return AddPrerequisiteOutcome.Duplicate;
        }

        if (PrerequisiteGraph.WouldCreateCycle(existing, candidate))
        {
            return AddPrerequisiteOutcome.WouldCreateCycle;
        }

        return await _store.AddAsync(candidate, cancellationToken)
            ? AddPrerequisiteOutcome.Added
            : AddPrerequisiteOutcome.Duplicate;
    }

    public ValueTask<IReadOnlyList<AssignmentPrerequisite>> GetForAssignmentAsync(
        Guid assignmentId,
        CancellationToken cancellationToken = default)
    {
        if (assignmentId == Guid.Empty)
        {
            throw new ArgumentException("Assignment ID is required.", nameof(assignmentId));
        }

        return _store.GetForAssignmentAsync(assignmentId, cancellationToken);
    }

    public ValueTask<bool> RemoveAsync(
        Guid assignmentId,
        Guid requiredAssignmentId,
        CancellationToken cancellationToken = default)
    {
        if (assignmentId == Guid.Empty || requiredAssignmentId == Guid.Empty)
        {
            throw new ArgumentException("Assignment IDs are required.");
        }

        return _store.RemoveAsync(assignmentId, requiredAssignmentId, cancellationToken);
    }

    private async ValueTask<bool> BelongsToCourseAsync(
        Guid assignmentId,
        string courseId,
        CancellationToken cancellationToken)
    {
        Assignment? assignment = await _assignments.GetByIdAsync(assignmentId, cancellationToken);

        return assignment is not null &&
               string.Equals(assignment.CourseId, courseId, StringComparison.Ordinal);
    }
}
