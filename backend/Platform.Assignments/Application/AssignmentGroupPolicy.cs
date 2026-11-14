using Platform.Assignments.Domain.Enums;

namespace Platform.Assignments.Application;

public static class AssignmentGroupCodes
{
    public const string CourseStudent = "CourseStudent";
    public const string GroupMember = "GroupMember";
    public const string NotEnrolled = "NotEnrolled";
    public const string NotGroupMember = "NotGroupMember";
    public const string UnsupportedScope = "UnsupportedScope";
}

public sealed record AssignmentGroupDecision(bool Allowed, string Code);

public static class AssignmentGroupPolicy
{
    public static AssignmentGroupDecision DecideVisibility(
        AssignmentScope scope,
        bool isEnrolled,
        bool isGroupMember)
    {
        if (scope != AssignmentScope.Course && scope != AssignmentScope.Group)
        {
            return new AssignmentGroupDecision(false, AssignmentGroupCodes.UnsupportedScope);
        }

        if (!isEnrolled)
        {
            return new AssignmentGroupDecision(false, AssignmentGroupCodes.NotEnrolled);
        }

        if (scope == AssignmentScope.Course)
        {
            return new AssignmentGroupDecision(true, AssignmentGroupCodes.CourseStudent);
        }

        return isGroupMember
            ? new AssignmentGroupDecision(true, AssignmentGroupCodes.GroupMember)
            : new AssignmentGroupDecision(false, AssignmentGroupCodes.NotGroupMember);
    }
}