using Platform.Access.Contracts;
using Platform.Access.Domain.Enums;
using Platform.Access.Domain.Models;

namespace Platform.Audit.Application.Authorization;

/// <summary>
/// S-08: singleton-safe policy registered through the public Access composition hook.
/// Subject state comes from the trusted server-created AccessRequest. The membership
/// provider must return current effective grants, excluding expired/revoked delegations.
/// </summary>
internal sealed class CourseAuditReadPolicy(IMembershipReader memberships) : IAccessPolicy
{
    public const string ActionId = "audit.events.read";

    public string Id => "audit.course.read";
    public string Version => "v1";

    public async ValueTask<bool> EvaluateAsync(
        AccessRequest request,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (request is null
            || request.Action != ActionId
            || request.Subject is not { State: AccountState.Active } subject
            || !Guid.TryParse(subject.Id, out var accountId)
            || accountId == Guid.Empty
            || request.Resource is not { Type: "course" } resource
            || !Guid.TryParseExact(resource.Id, "D", out var courseId)
            || courseId == Guid.Empty
            || resource.Id != courseId.ToString("D"))
        {
            return false;
        }

        try
        {
            // No caller state or rights cache in this singleton. The owner of this
            // Access contract resolves account/course membership and grant freshness.
            var membership = await memberships.GetAsync(accountId, courseId, cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            if (membership is not { Status: MembershipStatus.Active }
                || membership.AccountId != accountId
                || membership.CourseId != courseId)
            {
                return false;
            }

            if (membership.Role == CourseRole.Teacher)
                return true;

            if (membership.Role != CourseRole.Assistant || membership.DelegatedActions is null)
                return false;

            // Do not inherit a provider collection's potentially case-insensitive comparer.
            return membership.DelegatedActions.Any(action =>
                string.Equals(action, ActionId, StringComparison.Ordinal));
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception)
        {
            // Unavailable or malformed provider data never grants access. No rejected
            // payload, identifier or exception text is copied to an HTTP response/log.
            return false;
        }
    }
}
