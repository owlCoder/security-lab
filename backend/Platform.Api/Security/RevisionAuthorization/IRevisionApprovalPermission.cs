namespace Platform.Api.Security.RevisionAuthorization;

/// <summary>
/// Checks whether a user has explicit permission to approve revisions.
/// </summary>
public interface IRevisionApprovalPermission
{
    ValueTask<bool> CanApproveAsync(
        Guid userId,
        Guid revisionId,
        CancellationToken cancellationToken = default);
}
