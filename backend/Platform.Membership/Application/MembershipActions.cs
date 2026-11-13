namespace Platform.Membership.Application;
/// <summary>
/// Akcije koje modul za članstvo registruje u katalogu Access modula (ACC-05).
/// Nepoznata ili neregistrovana akcija se uvek odbija.
/// </summary>
public static class MembershipActions
{
    public const string EnrollMember = "course.member.enroll";
    public const string CourseResourceType = "course";
}