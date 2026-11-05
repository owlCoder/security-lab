namespace Platform.LabLocalization.Application;

/// <summary>What LAB-07 needs to know about a revision. Grading comes from here, never from a translation.</summary>
public sealed record RevisionInfo(Guid RevisionId, string EvaluatorVersion);

/// <summary>
/// LAB-07's view of the revision module. The real implementation is an adapter
/// over LabRevision, added once the revision exposes its evaluator.
/// </summary>
public interface ILabRevisionReader
{
    /// <summary>
    /// The published revision currently in use for the laboratory, or null if there is none.
    /// A draft is never returned, so its text cannot reach a student through this module.
    /// </summary>
    Task<RevisionInfo?> FindCurrentForLabAsync(Guid labId, CancellationToken ct = default);

    /// <summary>A revision of this laboratory in any state, or null if it does not exist or belongs to another laboratory.</summary>
    Task<RevisionInfo?> FindForLabAsync(Guid labId, Guid revisionId, CancellationToken ct = default);
}
