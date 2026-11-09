using Platform.AssignmentOverview.Contracts;
using Platform.Assignments.Contracts;
using Platform.Assignments.Domain.Models;

namespace Platform.AssignmentOverview.Infrastructure;

// Izvan Developmenta nema podataka dok se ne povezu pravi moduli; ne vracamo izmisljene podatke.
public sealed class UnavailableStudentCourseReader : IStudentCourseReader
{
    public ValueTask<IReadOnlyCollection<string>> GetActiveCourseIdsAsync(
        string studentId,
        CancellationToken cancellationToken = default)
        => throw Unavailable.Error("IStudentCourseReader");
}

public sealed class UnavailableAssignmentRepository : IAssignmentRepository
{
    public ValueTask<Assignment?> GetByIdAsync(Guid assignmentId, CancellationToken cancellationToken = default)
        => throw Unavailable.Error("IAssignmentRepository");

    public ValueTask<IReadOnlyList<Assignment>> GetForCoursesAsync(
        IReadOnlyCollection<string> courseIds,
        CancellationToken cancellationToken = default)
        => throw Unavailable.Error("IAssignmentRepository");

    public ValueTask AddAsync(Assignment assignment, CancellationToken cancellationToken = default)
        => throw Unavailable.Error("IAssignmentRepository");

    public ValueTask UpdateAsync(Assignment assignment, CancellationToken cancellationToken = default)
        => throw Unavailable.Error("IAssignmentRepository");
}

internal static class Unavailable
{
    public static DependencyUnavailableException Error(string contract) =>
        new(contract);
}
