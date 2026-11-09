using System.Collections.Frozen;
using Platform.Access.Domain.Enums;
using Platform.Access.Domain.Models;

namespace Platform.Access.Domain.Constants;

public static class CourseRolePolicies
{
    private static readonly IReadOnlySet<CourseRole> Students = Set(CourseRole.Student);
    private static readonly IReadOnlySet<CourseRole> Teachers = Set(CourseRole.Teacher);
    private static readonly IReadOnlySet<CourseRole> StudentsAndTeachers = Set(CourseRole.Student, CourseRole.Teacher);

    public static IReadOnlyList<CourseRolePolicyDefinition> All { get; } =
    [
        new(AccessActions.LabRead, StudentsAndTeachers, AllowAssistantDelegation: true),
        new(AccessActions.LabDraftManage, Teachers),
        new(AccessActions.LabRevisionPublish, Teachers),
        new(AccessActions.LabCriteriaHiddenRead, Teachers),
        new(AccessActions.AssignmentManage, Teachers),
        new(AccessActions.AssignmentReadOwn, Students, RequiresOwnership: true),
        new(AccessActions.AttemptCreate, Students),
        new(AccessActions.AttemptRead, Students, RequiresOwnership: true),
        new(AccessActions.AttemptSubmit, Students, RequiresOwnership: true),
        new(AccessActions.AttemptReadCourse, Teachers, AllowAssistantDelegation: true),
        new(AccessActions.AttemptReview, Teachers, AllowAssistantDelegation: true)
    ];

    private static IReadOnlySet<CourseRole> Set(params CourseRole[] roles) => roles.ToFrozenSet();
}
