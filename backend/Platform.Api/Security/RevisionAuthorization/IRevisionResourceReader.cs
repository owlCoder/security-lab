namespace Platform.Api.Security.RevisionAuthorization;

/// <summary>Server-owned authorization metadata; never supplied by the client.</summary>
public sealed record RevisionResource(Guid CourseId, Guid AuthorId, bool? RequiresIndependentApproval = true);

public interface IRevisionResourceReader
{
    Task<RevisionResource?> FindAsync(Guid revisionId, CancellationToken cancellationToken = default);
}
