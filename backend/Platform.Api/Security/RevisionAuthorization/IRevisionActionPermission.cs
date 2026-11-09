namespace Platform.Api.Security.RevisionAuthorization;

/// <summary>
/// Checks explicit permissions for revision actions.
/// The implementation must use trusted server-side authorization data.
/// account.unlock is a system action: resourceId is Guid.Empty. Its Identity
/// use case must validate the target account separately. Other actions require
/// a concrete revision ID, or a course ID for revision.create.
/// </summary>
public interface IRevisionActionPermission
{
    ValueTask<bool> IsAllowedAsync(
        Guid userId,
        string action,
        Guid resourceId,
        CancellationToken cancellationToken = default);
}
