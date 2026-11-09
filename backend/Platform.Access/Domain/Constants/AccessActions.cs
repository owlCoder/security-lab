using Platform.Access.Domain.Enums;
using Platform.Access.Domain.Models;

namespace Platform.Access.Domain.Constants;

public static class AccessActions
{
    public static IReadOnlySet<string> RequiredIds { get; } =
        new HashSet<string>(
            [
                "public.info.read",
                "public.catalog.read",
                "lab.read",
                "lab.draft.manage",
                "lab.revision.publish",
                "lab.criteria.hidden.read",
                "assignment.manage",
                "assignment.read.own",
                "course.read",
                "course.create",
                "course.archive",
                "group.read",
                "group.create",
                "group.member.add",
                "group.member.remove",
                "material.read",
                "attempt.create",
                "attempt.read",
                "attempt.submit",
                "attempt.read.course",
                "attempt.review",
                "account.status.change",
                "system.config.manage",
                "results.sensitive.read",
                "evaluation.task.process",
                "audit.events.read"
            ],
            StringComparer.Ordinal);

    public static IReadOnlyList<ActionDefinition> All =>
    [
        PublicInfoRead,
        PublicCatalogRead,
        LabRead,
        LabDraftManage,
        LabRevisionPublish,
        LabCriteriaHiddenRead,
        AssignmentManage,
        AssignmentReadOwn,
        CourseRead,
        CourseCreate,
        CourseArchive,
        GroupRead,
        GroupCreate,
        GroupMemberAdd,
        GroupMemberRemove,
        MaterialRead,
        AttemptCreate,
        AttemptRead,
        AttemptSubmit,
        AttemptReadCourse,
        AttemptReview,
        AccountStatusChange,
        SystemConfigManage,
        ResultsSensitiveRead,
        EvaluationTaskProcess,
        AuditEventsRead
    ];

    public static ActionDefinition PublicInfoRead { get; } =
        new("public.info.read", ActionScope.Public, null, false);

    public static ActionDefinition PublicCatalogRead { get; } =
        new("public.catalog.read", ActionScope.Public, null, false);

    public static ActionDefinition LabRead { get; } =
        new("lab.read", ActionScope.Course, "laboratory", false);

    public static ActionDefinition LabDraftManage { get; } =
        new("lab.draft.manage", ActionScope.Course, "laboratory", false);

    public static ActionDefinition LabRevisionPublish { get; } =
        new("lab.revision.publish", ActionScope.Course, "laboratory", true);

    public static ActionDefinition LabCriteriaHiddenRead { get; } =
        new("lab.criteria.hidden.read", ActionScope.Course, "laboratory", false);

    public static ActionDefinition AssignmentManage { get; } =
        new("assignment.manage", ActionScope.Course, "assignment", false);

    public static ActionDefinition AssignmentReadOwn { get; } =
        new("assignment.read.own", ActionScope.Course, "assignment", false);

    public static ActionDefinition CourseRead { get; } =
        new("course.read", ActionScope.Authenticated, null, false);

    public static ActionDefinition CourseCreate { get; } =
        new("course.create", ActionScope.Authenticated, null, true);

    public static ActionDefinition CourseArchive { get; } =
        new("course.archive", ActionScope.Authenticated, null, true);

    public static ActionDefinition GroupRead { get; } =
        new("group.read", ActionScope.Authenticated, null, false);

    public static ActionDefinition GroupCreate { get; } =
        new("group.create", ActionScope.Authenticated, null, true);

    public static ActionDefinition GroupMemberAdd { get; } =
        new("group.member.add", ActionScope.Authenticated, null, true);

    public static ActionDefinition GroupMemberRemove { get; } =
        new("group.member.remove", ActionScope.Authenticated, null, true);

    public static ActionDefinition MaterialRead { get; } =
        new("material.read", ActionScope.Authenticated, null, false);

    public static ActionDefinition AttemptCreate { get; } =
        new("attempt.create", ActionScope.Course, "assignment", false);

    public static ActionDefinition AttemptRead { get; } =
        new("attempt.read", ActionScope.Course, "attempt", false);

    public static ActionDefinition AttemptSubmit { get; } =
        new("attempt.submit", ActionScope.Course, "attempt", false);

    public static ActionDefinition AttemptReadCourse { get; } =
        new("attempt.read.course", ActionScope.Course, "attempt", false);

    public static ActionDefinition AttemptReview { get; } =
        new("attempt.review", ActionScope.Course, "attempt", true);

    public static ActionDefinition AccountStatusChange { get; } =
        new("account.status.change", ActionScope.System, null, true);

    public static ActionDefinition SystemConfigManage { get; } =
        new("system.config.manage", ActionScope.System, null, true);

    public static ActionDefinition ResultsSensitiveRead { get; } =
        new("results.sensitive.read", ActionScope.Course, "attempt", true);

    public static ActionDefinition EvaluationTaskProcess { get; } =
        new("evaluation.task.process", ActionScope.System, null, false);

    public static ActionDefinition AuditEventsRead { get; } =
        new("audit.events.read", ActionScope.Course, "course", false);
}
