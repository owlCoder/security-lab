using Platform.Access.Contracts;
using Platform.Access.Domain.Enums;
using Platform.Api.Interfaces.Identity;

namespace Platform.Api.Security.RevisionAuthorization;

/// <summary>
/// Bridges the public Access membership contract. Each evaluation reads current
/// membership in its own scope so singleton policies do not capture scoped services.
/// Missing membership never grants access. Global administrator status is used
/// only for account administration, never for course actions.
/// </summary>
public sealed class RevisionPermissions(IServiceScopeFactory scopes, IRevisionResourceReader revisions)
    : IRevisionActionPermission, IRevisionApprovalPermission
{
    public ValueTask<bool> CanApproveAsync(Guid userId, Guid revisionId,
        CancellationToken cancellationToken = default) =>
        IsAllowedAsync(userId, RevisionActions.Approve, revisionId, cancellationToken);

    public async ValueTask<bool> IsAllowedAsync(Guid userId, string action, Guid resourceId,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (userId == Guid.Empty)
            return false;

        await using var scope = scopes.CreateAsyncScope();
        if (action == RevisionActions.UnlockAccount)
            return scope.ServiceProvider.GetService<IAdminDirectory>()?.IsAdministrator(userId) == true;

        if (resourceId == Guid.Empty || !RevisionActions.IsCourseAction(action))
            return false;

        var revision = action == RevisionActions.Create
            ? null : await revisions.FindAsync(resourceId, cancellationToken);
        var courseId = action == RevisionActions.Create ? resourceId : revision?.CourseId;
        if (courseId is null || courseId == Guid.Empty ||
            (revision is not null && revision.AuthorId == Guid.Empty))
            return false;

        if ((action is RevisionActions.Submit or RevisionActions.Mine) && revision?.AuthorId != userId)
            return false;
        if (action == RevisionActions.Approve && (revision?.RequiresIndependentApproval is null ||
            (revision.RequiresIndependentApproval.Value && revision.AuthorId == userId)))
            return false;

        // An explicitly configured persistent grant source is authoritative. Never
        // fall back to role membership after a grant is revoked or expires.
        var grants = scope.ServiceProvider.GetService<IRevisionCoursePermissionReader>();
        if (grants is not null)
            return await grants.IsGrantedAsync(userId, courseId.Value, action, cancellationToken);

        var reader = scope.ServiceProvider.GetRequiredService<IMembershipReader>();
        var membership = await reader.GetAsync(userId, courseId.Value, cancellationToken);
        if (membership is null || membership.AccountId != userId ||
            membership.CourseId != courseId || membership.Status != MembershipStatus.Active)
            return false;

        return membership.Role == CourseRole.Teacher ||
            (membership.Role == CourseRole.Assistant && membership.DelegatedActions is not null &&
             membership.DelegatedActions.Any(granted => string.Equals(granted, action, StringComparison.Ordinal)));
    }
}
