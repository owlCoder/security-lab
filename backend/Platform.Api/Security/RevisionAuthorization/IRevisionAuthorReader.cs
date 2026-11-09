namespace Platform.Api.Security.RevisionAuthorization;

/// <summary>
/// Provides trusted information about the author of a revision.
/// The author must be loaded from server-side storage, never from the request body.
/// </summary>
public interface IRevisionAuthorReader
{
    Task<Guid?> FindAuthorIdAsync(
        Guid revisionId,
        CancellationToken cancellationToken = default);
}
