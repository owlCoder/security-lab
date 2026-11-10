using Platform.AssignmentPrerequisites.Contracts;
using Platform.AssignmentPrerequisites.Domain;

namespace Platform.AssignmentOverview.Infrastructure;

// ASN-07
// Izvan Developmenta nema podataka dok se ne povežu pravi moduli; ne vraćamo izmišljene podatke.
public sealed class UnavailableAttemptOutcomeReader : IAttemptOutcomeReader
{
    public ValueTask<IReadOnlyList<AttemptOutcomeSnapshot>> GetForStudentAsync(
        string studentId,
        IReadOnlyCollection<Guid> assignmentIds,
        CancellationToken cancellationToken = default)
        => throw Unavailable.Error("IAttemptOutcomeReader");
}

public sealed class UnavailableAssignmentPrerequisiteStore : IAssignmentPrerequisiteStore
{
    public ValueTask<IReadOnlyList<AssignmentPrerequisite>> GetForCourseAsync(
        string courseId,
        CancellationToken cancellationToken = default)
        => throw Unavailable.Error("IAssignmentPrerequisiteStore");

    public ValueTask<IReadOnlyList<AssignmentPrerequisite>> GetForAssignmentAsync(
        Guid assignmentId,
        CancellationToken cancellationToken = default)
        => throw Unavailable.Error("IAssignmentPrerequisiteStore");

    public ValueTask<bool> AddAsync(
        AssignmentPrerequisite prerequisite,
        CancellationToken cancellationToken = default)
        => throw Unavailable.Error("IAssignmentPrerequisiteStore");

    public ValueTask<bool> RemoveAsync(
        Guid assignmentId,
        Guid requiredAssignmentId,
        CancellationToken cancellationToken = default)
        => throw Unavailable.Error("IAssignmentPrerequisiteStore");
}