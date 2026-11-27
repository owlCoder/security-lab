namespace Team21.Acc.Core.Courses;

/// <summary>Answer of <see cref="ICourseEnrollmentPolicy"/> for one course.</summary>
public enum CourseEnrollmentDecision
{
    /// <summary>The course is active, a new enrollment may be created.</summary>
    Open,

    /// <summary>The course is archived. New enrollments are refused, existing ones stay.</summary>
    Archived,

    /// <summary>No such course, or it was removed.</summary>
    NotFound
}

/// <summary>
/// ACC-01 published contract: "archiving stops new enrollments and does not delete history".
/// Enrollment (ACC-02) and other modules ask this before they add a member, instead of
/// reading the courses schema themselves. It only reports the course state; who may enroll
/// whom stays with the caller's own authorization.
/// </summary>
public interface ICourseEnrollmentPolicy
{
    Task<CourseEnrollmentDecision> CheckAsync(
        Guid courseId,
        CancellationToken cancellationToken = default);
}
