using Platform.Membership.Infrastructure.Courses;
namespace Platform.Membership.Infrastructure.Development;
/// <summary>
/// PRIVREMENO (ACC-02): izmišljeni nastavnici za lokalni rad, dok ACC-03 (dodela nastavnika)
/// nema vlasnika. Studenti 2222... i 3333... već postoje u probnim nalozima iz Program.cs.
/// </summary>
public static class DevelopmentMembers
{
    public static readonly Guid Teacher = Guid.Parse("44444444-4444-4444-4444-444444444444");
    public static readonly Guid OtherTeacher = Guid.Parse("55555555-5555-5555-5555-555555555555");
    /// <summary>Ko "upisuje" nastavnike u probnim podacima: probni administrator.</summary>
    public static readonly Guid SeedActor = Guid.Parse("11111111-1111-1111-1111-111111111111");
    public static IReadOnlyList<(Guid CourseId, Guid TeacherId)> Teachers { get; } =
    [
    (DevelopmentCourses.SecurityLab, Teacher),
 (DevelopmentCourses.ArchivedCourse, Teacher),
 (DevelopmentCourses.OtherCourse, OtherTeacher)
    ];
}