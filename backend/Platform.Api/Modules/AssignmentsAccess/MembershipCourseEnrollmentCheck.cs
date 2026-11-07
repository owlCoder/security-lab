using Platform.Access.Contracts;
using Platform.Access.Domain.Enums;
using Platform.Assignments.Contracts;

namespace Platform.Api.Modules.AssignmentsAccess;

// ASN-06 TIM 15 (ACC-02, ACC-17)
// Implements ICourseEnrollmentCheck. Answers yes or no only; no membership data leaves the module.
// "Yes" means an active membership with the Student role in exactly this course.
//
// Open point (see docs/contracts/ASN-06-contracts.md): Assignment.CourseId is a string while
// Platform.Access identifies courses with a GUID. Until the teams agree on one type, a course ID
// that is not a GUID is answered with "no" (fail closed) instead of being guessed.
public sealed class MembershipCourseEnrollmentCheck(IMembershipReader memberships) : ICourseEnrollmentCheck
{
    public async ValueTask<bool> IsActiveStudentAsync(
        string studentId,
        string courseId,
        CancellationToken cancellationToken = default)
    {
        if (!Guid.TryParse(studentId, out var account) || account == Guid.Empty ||
            !Guid.TryParse(courseId, out var course) || course == Guid.Empty)
        {
            return false;
        }

        try
        {
            var membership = await memberships.GetAsync(account, course, cancellationToken).ConfigureAwait(false);

            return membership is not null &&
                   membership.AccountId == account &&
                   membership.CourseId == course &&
                   membership.Status == MembershipStatus.Active &&
                   membership.Role == CourseRole.Student;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception)
        {
            return false;
        }
    }
}
