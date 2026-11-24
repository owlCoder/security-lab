using System.Text.RegularExpressions;
using Platform.Access.Domain.Constants;
using Platform.Access.Domain.Enums;
using Platform.Access.Domain.Models;

namespace Platform.Access.Infrastructure.Guards;

public sealed class OwnershipScopeGuard
{
    private static readonly IReadOnlySet<AccessRelation> NoRelations =
        new HashSet<AccessRelation>();

    private static readonly Regex TraceIdPattern =
        new(@"^[a-z0-9._:-]{1,64}\z", RegexOptions.CultureInvariant);

    public PolicyResult Evaluate(EvaluationSnapshot snapshot, string action)
    {
        ArgumentNullException.ThrowIfNull(snapshot);

        if (snapshot.Resource is null)
            return Deny(AccessDenialReason.MissingAttribute,
                [Step("resource.present", TraceResult.Failed)]);

        var allowed = GetAllowedRelations(action);
        if (allowed.Count == 0)
            return Deny(AccessDenialReason.MissingAttribute,
                [Step("action.relations", TraceResult.Failed)]);

        var resource = snapshot.Resource;
        if (resource.CourseId is null || resource.CourseId == Guid.Empty)
            return Deny(AccessDenialReason.MissingAttribute,
                [Step("resource.course", TraceResult.Failed)]);

        var actionDefinition = CourseRolePolicies.All
            .FirstOrDefault(value => string.Equals(value.Action.Id, action, StringComparison.Ordinal))
            ?.Action ?? AccessActions.All.FirstOrDefault(
                value => string.Equals(value.Id, action, StringComparison.Ordinal));
        if (actionDefinition is not null &&
            !string.Equals(resource.Type, actionDefinition.ResourceType, StringComparison.Ordinal))
            return Deny(AccessDenialReason.ContextMismatch,
                [Step("resource.type", TraceResult.Failed)]);

        if (!Guid.TryParseExact(snapshot.Subject.Id, "D", out var subjectId))
            return Deny(AccessDenialReason.MissingAttribute,
                [Step("subject.id", TraceResult.Failed)]);

        if (RequiresOwnership(action))
        {
            if (resource.OwnerId is null || resource.OwnerId == Guid.Empty)
                return Deny(AccessDenialReason.MissingAttribute,
                    [Step("resource.owner", TraceResult.Failed)]);

            if (resource.OwnerId != subjectId)
                return Deny(AccessDenialReason.NotPermitted,
                    [Step("ownership.owner", TraceResult.Failed)]);
        }

        if (string.Equals(action, "results.sensitive.read", StringComparison.Ordinal))
            return Deny(AccessDenialReason.NotPermitted,
                [Step("grant.holder", TraceResult.Failed)]);

        var membership = snapshot.Membership;
        if (membership is not null &&
            membership.CourseId != Guid.Empty &&
            membership.CourseId != resource.CourseId)
            return Deny(AccessDenialReason.ContextMismatch,
                [Step("course.scope", TraceResult.Failed)]);

        if (membership is null ||
            membership.AccountId != subjectId ||
            membership.CourseId != resource.CourseId ||
            membership.Status != MembershipStatus.Active)
            return Deny(AccessDenialReason.NotPermitted,
                [Step("membership.active", TraceResult.Failed)]);

        AccessRelation? relation = membership.Role switch
        {
            CourseRole.Student => AccessRelation.CourseStudent,
            CourseRole.Teacher => AccessRelation.CourseTeacher,
            CourseRole.Assistant when membership.DelegatedActions is not null &&
                                      membership.DelegatedActions.Contains(action)
                => AccessRelation.CourseAssistant,
            _ => null
        };

        if (relation is null || !allowed.Contains(relation.Value))
            return Deny(AccessDenialReason.NotPermitted,
                [Step("relation.allowed", TraceResult.Failed)]);

        return Permit([
            Step("resource.course", TraceResult.Passed),
            Step("membership.active", TraceResult.Passed),
            Step("relation.allowed", TraceResult.Passed),
            .. RequiresOwnership(action)
                ? [Step("ownership.owner", TraceResult.Passed)]
                : Array.Empty<TraceStep>()
        ]);
    }

    private static IReadOnlySet<AccessRelation> GetAllowedRelations(string action)
    {
        if (string.Equals(action, "results.sensitive.read", StringComparison.Ordinal))
        {
            // GrantHolder has no implementation in this User Story and is fail-closed.
            return new HashSet<AccessRelation> { AccessRelation.GrantHolder };
        }

        var definition = CourseRolePolicies.All.FirstOrDefault(
            value => string.Equals(value.Action.Id, action, StringComparison.Ordinal));
        if (definition is null)
            return NoRelations;

        var relations = new HashSet<AccessRelation>();
        foreach (var role in definition.AllowedRoles)
        {
            if (role == CourseRole.Student)
                relations.Add(AccessRelation.CourseStudent);
            if (role == CourseRole.Teacher)
                relations.Add(AccessRelation.CourseTeacher);
        }

        if (definition.AllowAssistantDelegation)
            relations.Add(AccessRelation.CourseAssistant);

        return relations;
    }

    private static bool RequiresOwnership(string action)
    {
        var definition = CourseRolePolicies.All.FirstOrDefault(
            value => string.Equals(value.Action.Id, action, StringComparison.Ordinal));
        return definition?.RequiresOwnership == true;
    }

    private static PolicyResult Permit(IReadOnlyList<TraceStep> steps) =>
        new(PolicyOutcome.Permit, AccessDenialReason.None, new DecisionTrace(steps));

    private static PolicyResult Deny(AccessDenialReason reason, IReadOnlyList<TraceStep> steps) =>
        new(PolicyOutcome.Deny, reason, new DecisionTrace(steps));

    private static TraceStep Step(string id, TraceResult result)
    {
        if (!TraceIdPattern.IsMatch(id))
            throw new InvalidOperationException("Invalid decision trace step identifier.");
        return new TraceStep(id, result);
    }
}
