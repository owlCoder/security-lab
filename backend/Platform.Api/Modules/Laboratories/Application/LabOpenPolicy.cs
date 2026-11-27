using Platform.LabVisibility.Contracts;

namespace Platform.Api.Modules.Laboratories.Application;

// LAB-03: stabilni kodovi odluke pri otvaranju. Klijent ih ne vidi; pisu se u serverski log.
public static class LabOpenCodes
{
    public const string CourseStudent = "CourseStudent";
    public const string CourseTeacher = "CourseTeacher";
    public const string NoCourse = "NoCourse";
    public const string NotCourseMember = "NotCourseMember";
    public const string NotOwner = "NotOwner";
}

public sealed record LabOpenDecision(bool Allowed, string Code);

// LAB-03: ko sme da otvori laboratoriju. Kurs dolazi iz sacuvane laboratorije, nikad iz zahteva.
// Podrazumevano odbijanje: dozvoljeni su samo student kursa i nastavnik kursa koji je autor.
public static class LabOpenPolicy
{
    public static LabOpenDecision DecideOpen(Guid courseId, CourseRole roleOnCourse, bool isCreator)
    {
        // Laboratorija bez kursa je van dozvoljenog konteksta i ne otvara se nikome.
        if (courseId == Guid.Empty)
        {
            return new LabOpenDecision(false, LabOpenCodes.NoCourse);
        }

        if (roleOnCourse == CourseRole.Student)
        {
            return new LabOpenDecision(true, LabOpenCodes.CourseStudent);
        }

        // Vlasnistvo iz LAB-01 vazi za nastavnike: nastavnik vidi samo svoje laboratorije.
        if (roleOnCourse == CourseRole.Teacher)
        {
            return isCreator
                ? new LabOpenDecision(true, LabOpenCodes.CourseTeacher)
                : new LabOpenDecision(false, LabOpenCodes.NotOwner);
        }

        return new LabOpenDecision(false, LabOpenCodes.NotCourseMember);
    }
}
