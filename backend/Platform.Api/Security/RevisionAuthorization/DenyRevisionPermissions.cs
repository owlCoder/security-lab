namespace Platform.Api.Security.RevisionAuthorization;

/// <summary>
/// Fail-closed fallback until trusted permission providers are connected.
/// Never grants access.
/// </summary>
public sealed class DenyRevisionPermissions :
    IRevisionApprovalPermission,
    IRevisionActionPermission
{
    public ValueTask<bool> CanApproveAsync(
        Guid userId,
        Guid revisionId,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return ValueTask.FromResult(false);
    }

    public ValueTask<bool> IsAllowedAsync(
        Guid userId,
        string action,
        Guid resourceId,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return ValueTask.FromResult(false);
    }
}
