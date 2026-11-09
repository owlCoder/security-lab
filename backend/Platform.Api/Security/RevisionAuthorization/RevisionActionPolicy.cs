using Platform.Access.Contracts;
using Platform.Access.Domain.Enums;
using Platform.Access.Domain.Models;

namespace Platform.Api.Security.RevisionAuthorization;

public sealed class RevisionActionPolicy(
    IRevisionActionPermission permission) : IAccessPolicy
{
    public string Id => "rev05.explicit-actions";
    public string Version => "v1";

    public async ValueTask<bool> EvaluateAsync(
        AccessRequest request,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (request.Subject is null ||
            request.Subject.State != AccountState.Active ||
            !Guid.TryParse(request.Subject.Id, out var userId) ||
            userId == Guid.Empty)
        {
            return false;
        }

        // The public Access catalog forbids resources on system actions.
        // The Identity use case owns target existence and account-state validation.
        if (request.Action == RevisionActions.UnlockAccount)
            return request.Resource is null && await permission.IsAllowedAsync(
                userId, request.Action, Guid.Empty, cancellationToken);

        string expectedResourceType = request.Action switch
        {
            RevisionActions.Create => "course",
            RevisionActions.Read => "revision",
            RevisionActions.Mine => "revision",
            RevisionActions.PendingReviews => "revision",
            RevisionActions.Submit => "revision",
            RevisionActions.Return => "revision",
            _ => ""
        };

        if (expectedResourceType.Length == 0 ||
            request.Resource?.Type != expectedResourceType ||
            !Guid.TryParse(request.Resource.Id, out var resourceId) ||
            resourceId == Guid.Empty)
        {
            return false;
        }

        return await permission.IsAllowedAsync(
            userId,
            request.Action,
            resourceId,
            cancellationToken);
    }
}

