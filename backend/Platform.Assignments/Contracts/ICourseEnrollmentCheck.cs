namespace Platform.Assignments.Contracts;

// ASN-06 TIM 15
// Answers only yes or no, so the Assignments module never reads membership storage (ACC-17).
public interface ICourseEnrollmentCheck
{
    ValueTask<bool> IsActiveStudentAsync(
        string studentId,
        string courseId,
        CancellationToken cancellationToken = default);
}
