using Platform.Access.Domain.Enums;
using Platform.Access.Domain.Models;

namespace Platform.Access.Infrastructure.Grants;

public sealed class ElevatedGrantValidator : IElevatedGrantValidator
{
    public bool IsValid(
        ElevatedGrant grant,
        Guid holderId,
        string action,
        ResourceDescriptor resource,
        DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(grant);
        ArgumentNullException.ThrowIfNull(resource);

        return !grant.Revoked
            && grant.HolderId == holderId
            && grant.IssuerId != holderId
            && string.Equals(grant.Action, action, StringComparison.Ordinal)
            && !string.IsNullOrWhiteSpace(grant.Reason)
            && grant.NotBefore <= now
            && now < grant.ExpiresAt
            && CoversResource(grant.Scope, resource);
    }

    private static bool CoversResource(GrantScope scope, ResourceDescriptor resource) =>
        scope.Kind switch
        {
            GrantScopeKind.Resource => string.Equals(scope.Id, resource.Id, StringComparison.Ordinal),
            GrantScopeKind.Course => resource.CourseId is { } courseId
                && courseId != Guid.Empty
                && Guid.TryParseExact(scope.Id, "D", out var scopeCourseId)
                && scopeCourseId == courseId,
            _ => false
        };
}
