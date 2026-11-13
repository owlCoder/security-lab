using Platform.Membership.Domain;
namespace Platform.Membership.Application;

public enum EnrollmentOutcome
{
    Enrolled,
    Unauthenticated,
    Forbidden,
    InvalidInput,
    CourseNotFound,
    CourseArchived,
    AccountNotFound,
    AccountNotActive,
    AlreadyEnrolled
}
public sealed record EnrollmentResult(EnrollmentOutcome Outcome, CourseMember? Member = null)
{
    public static EnrollmentResult Of(EnrollmentOutcome outcome) => new(outcome);
    public static EnrollmentResult Enrolled(CourseMember member) => new(EnrollmentOutcome.Enrolled, member);
}