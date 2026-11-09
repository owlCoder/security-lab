namespace Team21.Acc.Core.Courses;

/// <summary>Life cycle of a course. Stored by name, starts at 1 so an unset value is never valid.</summary>
public enum CourseStatus
{
    Active = 1,
    Archived = 2
}
