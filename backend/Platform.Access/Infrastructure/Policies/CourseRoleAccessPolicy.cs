using Platform.Access.Contracts;
using Platform.Access.Domain.Models;
using Platform.Access.Domain.Enums;

namespace Platform.Access.Infrastructure.Policies;

public sealed class CourseRoleAccessPolicy(
    CourseRolePolicyDefinition definition,
    IMembershipReader membershipReader,
    IResourceDescriptorResolver resourceDescriptorResolver) : IAccessPolicy
{
    public string Id => $"course-role:{definition.Action.Id}";
    public string Version => "1.0";

    public async ValueTask<bool> EvaluateAsync(
        AccessRequest request,
        CancellationToken cancellationToken = default)
    {
        if (!string.Equals(request.Action, definition.Action.Id, StringComparison.Ordinal) ||
            request.Subject is null || request.Resource is null)
        {
            return false;
        }

        if (!string.Equals(request.Resource.Type, definition.Action.ResourceType, StringComparison.Ordinal))
        {
            return false;
        }

        if (!Guid.TryParse(request.Subject.Id, out var accountId))
        {
            return false;
        }

        if (definition.RequiresOwnership)
        {
            if (resourceDescriptorResolver is not IResourceOwnershipResolver ownershipResolver)
            {
                return false;
            }

            var ownerId = await ownershipResolver.ResolveOwnerIdAsync(
                request.Resource,
                cancellationToken);

            if (ownerId is null || ownerId == Guid.Empty || ownerId != accountId)
            {
                return false;
            }
        }

        var courseId = await resourceDescriptorResolver.ResolveCourseIdAsync(
            request.Resource,
            cancellationToken);

        if (courseId is null || courseId == Guid.Empty)
        {
            return false;
        }

        var membership = await membershipReader.GetAsync(
            accountId,
            courseId.Value,
            cancellationToken);

        if (membership is null ||
            membership.AccountId != accountId ||
            membership.CourseId != courseId.Value ||
            membership.Status != MembershipStatus.Active)
        {
            return false;
        }

        if (membership.Role == CourseRole.Assistant)
        {
            return definition.AllowAssistantDelegation &&
                   membership.DelegatedActions is not null &&
                   membership.DelegatedActions.Any(action =>
                       string.Equals(action, definition.Action.Id, StringComparison.Ordinal));
        }

        return definition.AllowedRoles.Contains(membership.Role);
    }
}
