using Platform.Assignments.Domain.Models;

namespace Platform.AssignmentOverview.Infrastructure;

// ASN-02: privremeni podaci za lokalni razvoj dok moduli za upise i dodele nemaju implementaciju.
public static class DevelopmentAssignmentData
{
    // Isti ID kao nalog student@example.com iz razvojnog seed-a identiteta.
    public const string StudentId = "22222222-2222-2222-2222-222222222222";

    public const string CourseOib = "course-oib";
    public const string CourseSecurity = "course-security";
    public const string CourseOther = "course-other";

    public static readonly Guid OibAssignmentId = Guid.Parse("a0000000-0000-0000-0000-000000000001");
    public static readonly Guid SecurityAssignmentId = Guid.Parse("a0000000-0000-0000-0000-000000000002");
    public static readonly Guid OtherCourseAssignmentId = Guid.Parse("a0000000-0000-0000-0000-000000000003");

    private static readonly DateTimeOffset Due = new(2027, 1, 31, 22, 59, 0, TimeSpan.Zero);

    public static IReadOnlyDictionary<string, string[]> Enrollments { get; } =
        new Dictionary<string, string[]>
        {
            [StudentId] = [CourseOib, CourseSecurity]
        };

    public static IReadOnlyList<Assignment> CreateAssignments() =>
    [
        new Assignment(OibAssignmentId, CourseOib, "rev-sql-injection-1", null, Due, 3),
        new Assignment(SecurityAssignmentId, CourseSecurity, "rev-xss-1", null, Due, 2),

        // Kurs na koji student nije upisan; ne sme da se pojavi u njegovoj listi.
        new Assignment(OtherCourseAssignmentId, CourseOther, "rev-csrf-1", null, Due, 1),

        // ASN-07: dodele sa preduslovima, za prikaz „zaključano“.
        .. DevelopmentLockData.CreateAssignments()
    ];
}
