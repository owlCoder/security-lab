using Platform.LabVisibility.Contracts;

namespace Platform.Api.Modules.Laboratories.Application;

// LAB-03: stabilni kodovi odluke. Klijent ih ne vidi; pisu se u serverski log.
public static class LabCourseCodes
{
    public const string CourseTeacher = "CourseTeacher";
    public const string NotCourseTeacher = "NotCourseTeacher";
}

public sealed record LabCourseDecision(bool Allowed, string Code);

// LAB-03: ko sme da napravi laboratoriju na kursu.
// Odlucuje uloga NA TOM KURSU (ICourseMembership), ne globalna uloga na platformi.
// Podrazumevano odbijanje: samo nastavnik kursa je dozvoljen.
public static class LabCoursePolicy
{
    public static LabCourseDecision DecideCreate(CourseRole roleOnCourse)
    {
        if (roleOnCourse == CourseRole.Teacher)
        {
            return new LabCourseDecision(true, LabCourseCodes.CourseTeacher);
        }

        return new LabCourseDecision(false, LabCourseCodes.NotCourseTeacher);
    }
}
