using Team21.Acc.Core.Common;
using Team21.Acc.Core.Groups;
using Team21.Acc.Core.Groups.Visibility;
using Team21.Acc.Core.Groups.Visibility.Interfaces;
using Team21.Acc.Core.Interfaces;

namespace Team21.Acc.Infrastructure.Groups.Services;

/// <summary>
/// Server-side material visibility (ACC-06). The course and groups always come from the material's
/// rule, never from the request. Rights are read again on every call. In an archived course only the
/// teacher and a reviewing assistant still see materials.
/// </summary>
public class MaterialVisibilityService : IMaterialVisibilityService
{
    private static readonly IReadOnlySet<Guid> NoGroups = new HashSet<Guid>();

    private readonly ICurrentActor _actor;
    private readonly ICorrelationContext _correlation;
    private readonly IAccountStatusReader _accounts;
    private readonly ICourseMembershipReader _courseMembers;
    private readonly ICourseStateReader _courseStates;
    private readonly IMaterialAccessRuleReader _rules;
    private readonly IGroupMembershipReader _groups;
    private readonly IVisibilityAuditWriter _audit;
    private readonly TimeProvider _time;

    public MaterialVisibilityService(
        ICurrentActor actor,
        ICorrelationContext correlation,
        IAccountStatusReader accounts,
        ICourseMembershipReader courseMembers,
        ICourseStateReader courseStates,
        IMaterialAccessRuleReader rules,
        IGroupMembershipReader groups,
        IVisibilityAuditWriter audit,
        TimeProvider time)
    {
        _actor = actor;
        _correlation = correlation;
        _accounts = accounts;
        _courseMembers = courseMembers;
        _courseStates = courseStates;
        _rules = rules;
        _groups = groups;
        _audit = audit;
        _time = time;
    }

    public async Task<Result> CanAccessAsync(MaterialRef? material, MaterialAction action, CancellationToken ct = default)
    {
        if (_actor.UserId is not { } userId)
            return Error.Unauthorized("Sign in to see materials.");

        if (material is null)
            return Error.NotFound("Material not found.");

        if (!Enum.IsDefined(action))
            return await DenyMaterialAsync(userId, null, material, action, DenialReason.UnknownAction, ct);

        if (await _accounts.GetStatusAsync(userId, ct) != AccountStatus.Active)
            return await DenyMaterialAsync(userId, null, material, action, DenialReason.AccountNotActive, ct);

        var rule = await _rules.FindAsync(material, ct);
        if (rule is null)
            return await DenyMaterialAsync(userId, null, material, action, DenialReason.NoRule, ct);

        var membership = await _courseMembers.FindAsync(userId, rule.CourseId, ct);
        if (membership is not { Active: true } || membership.CourseId != rule.CourseId)
            return await DenyMaterialAsync(userId, rule.CourseId, material, action, DenialReason.NotCourseMember, ct);

        var courseDenial = await CheckCourseAsync(rule.CourseId, membership, ct);
        if (courseDenial is not null)
            return await DenyMaterialAsync(userId, rule.CourseId, material, action, courseDenial.Value, ct);

        if (SeesAll(membership))
            return Result.Success();

        var groupIds = rule.Visibility == MaterialVisibility.RestrictedToGroups
            ? await ActiveGroupIdsAsync(userId, rule.CourseId, ct)
            : NoGroups;

        var reason = Check(rule, groupIds);
        return reason is null
            ? Result.Success()
            : await DenyMaterialAsync(userId, rule.CourseId, material, action, reason.Value, ct);
    }

    public async Task<Result<IReadOnlyList<MaterialRef>>> FilterVisibleAsync(Guid courseId, CancellationToken ct = default)
    {
        if (_actor.UserId is not { } userId)
            return Error.Unauthorized("Sign in to see materials.");

        if (await _accounts.GetStatusAsync(userId, ct) != AccountStatus.Active)
            return await DenyCourseAsync(userId, courseId, DenialReason.AccountNotActive, ct);

        var membership = await _courseMembers.FindAsync(userId, courseId, ct);
        if (membership is not { Active: true } || membership.CourseId != courseId)
            return await DenyCourseAsync(userId, courseId, DenialReason.NotCourseMember, ct);

        var courseDenial = await CheckCourseAsync(courseId, membership, ct);
        if (courseDenial is not null)
            return await DenyCourseAsync(userId, courseId, courseDenial.Value, ct);

        var rules = (await _rules.ListByCourseAsync(courseId, ct))
            .Where(r => r.CourseId == courseId)
            .ToList();

        if (SeesAll(membership))
            return Result<IReadOnlyList<MaterialRef>>.Success(rules.Select(r => r.Material).ToList());

        var groupIds = rules.Any(r => r.Visibility == MaterialVisibility.RestrictedToGroups)
            ? await ActiveGroupIdsAsync(userId, courseId, ct)
            : NoGroups;

        var visible = rules
            .Where(r => Check(r, groupIds) is null)
            .Select(r => r.Material)
            .ToList();

        return Result<IReadOnlyList<MaterialRef>>.Success(visible);
    }

    // Teacher, or assistant with delegated review
    private static bool SeesAll(CourseMembership membership) =>
        membership.Role == CourseRole.Teacher
        || (membership.Role == CourseRole.Assistant && membership.CanReview);

    // null means the course is usable for this member
    private async Task<DenialReason?> CheckCourseAsync(Guid courseId, CourseMembership membership, CancellationToken ct) =>
        await _courseStates.GetStateAsync(courseId, ct) switch
        {
            CourseState.Active => null,
            CourseState.Archived when SeesAll(membership) => null,
            CourseState.Archived => DenialReason.CourseArchived,
            _ => DenialReason.UnknownCourse
        };

    // null means visible
    private static DenialReason? Check(MaterialAccessRule rule, IReadOnlySet<Guid> groupIds) =>
        rule.Visibility switch
        {
            MaterialVisibility.CourseWide => null,
            MaterialVisibility.RestrictedToGroups when rule.AllowedGroupIds.Any(groupIds.Contains) => null,
            MaterialVisibility.RestrictedToGroups => DenialReason.NotInGroup,
            _ => DenialReason.UnknownVisibility
        };

    private async Task<IReadOnlySet<Guid>> ActiveGroupIdsAsync(Guid userId, Guid courseId, CancellationToken ct)
    {
        var groups = await _groups.GroupsOfAsync(userId, courseId, ct);

        return groups
            .Where(g => g.CourseId == courseId)
            .Select(g => g.GroupId)
            .ToHashSet();
    }

    private async Task<Result> DenyMaterialAsync(
        Guid userId, Guid? courseId, MaterialRef material, MaterialAction action, DenialReason reason, CancellationToken ct)
    {
        await WriteDenialAsync(userId, courseId, material, action, reason, ct);
        return Error.NotFound("Material not found.");
    }

    private async Task<Error> DenyCourseAsync(Guid userId, Guid courseId, DenialReason reason, CancellationToken ct)
    {
        await WriteDenialAsync(userId, courseId, null, null, reason, ct);
        return Error.NotFound("Course not found.");
    }

    private Task WriteDenialAsync(
        Guid userId, Guid? courseId, MaterialRef? material, MaterialAction? action, DenialReason reason, CancellationToken ct) =>
        _audit.WriteDenialAsync(
            new VisibilityDenial(userId, courseId, material, action, _time.GetUtcNow().UtcDateTime, reason, _correlation.CorrelationId),
            ct);
}
