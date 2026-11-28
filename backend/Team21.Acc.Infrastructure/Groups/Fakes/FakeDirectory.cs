using Team21.Acc.Core.Groups.Visibility;

namespace Team21.Acc.Infrastructure.Groups.Fakes;

/// <summary>
/// Development only. In-memory accounts, course memberships and course states that other modules own.
/// Seeded with <see cref="DemoIds"/>.
/// </summary>
public sealed class FakeDirectory
{
    private readonly Dictionary<Guid, AccountStatus> _accounts = new()
    {
        [DemoIds.Teacher] = AccountStatus.Active,
        [DemoIds.Assistant] = AccountStatus.Active,
        [DemoIds.StudentA] = AccountStatus.Active,
        [DemoIds.StudentB] = AccountStatus.Active,
        [DemoIds.BlockedStudent] = AccountStatus.Blocked,
        [DemoIds.Outsider] = AccountStatus.Active
    };

    private readonly Dictionary<(Guid AccountId, Guid CourseId), CourseMembership> _memberships = new()
    {
        [(DemoIds.Teacher, DemoIds.ArchivedCourse)] = new(DemoIds.ArchivedCourse, CourseRole.Teacher, Active: true, CanReview: true),
        [(DemoIds.StudentA, DemoIds.ArchivedCourse)] = new(DemoIds.ArchivedCourse, CourseRole.Student, Active: true, CanReview: false),
        [(DemoIds.Teacher, DemoIds.Course)] = new(DemoIds.Course, CourseRole.Teacher, Active: true, CanReview: true),
        [(DemoIds.Assistant, DemoIds.Course)] = new(DemoIds.Course, CourseRole.Assistant, Active: true, CanReview: true),
        [(DemoIds.StudentA, DemoIds.Course)] = new(DemoIds.Course, CourseRole.Student, Active: true, CanReview: false),
        [(DemoIds.StudentB, DemoIds.Course)] = new(DemoIds.Course, CourseRole.Student, Active: true, CanReview: false),
        [(DemoIds.BlockedStudent, DemoIds.Course)] = new(DemoIds.Course, CourseRole.Student, Active: true, CanReview: false)
    };

    // Unknown accounts count as inactive
    public AccountStatus StatusOf(Guid accountId) =>
        _accounts.GetValueOrDefault(accountId, AccountStatus.Inactive);


    public CourseMembership? MembershipOf(Guid accountId, Guid courseId) =>
        _memberships.GetValueOrDefault((accountId, courseId));
}
