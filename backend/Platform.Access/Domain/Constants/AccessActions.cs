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
                "attempt.create",
                "attempt.read",
                "attempt.submit",
                "attempt.read.course",
                "attempt.review",
                "account.status.change",
                "system.config.manage",
                "results.sensitive.read",
                "evaluation.task.process"
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
        AttemptCreate,
        AttemptRead,
        AttemptSubmit,
        AttemptReadCourse,
        AttemptReview,
        AccountStatusChange,
        SystemConfigManage,
        ResultsSensitiveRead,
        EvaluationTaskProcess
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
}
