namespace Platform.LabLocalization.Application;

/// <summary>The revision's own text, in the default language (Serbian). It is part of the revision, not a translation.</summary>
public sealed record RevisionText(string Title, string Description, string Instructions);

/// <summary>
/// What LAB-07 needs to know about a revision. Grading and the default-language text come from here,
/// never from a translation. <see cref="IsDraft"/> is true only while the revision can still be edited.
/// </summary>
public sealed record RevisionInfo(Guid RevisionId, string EvaluatorVersion, RevisionText Text, bool IsDraft);

/// <summary>LAB-07's view of the revision module.</summary>
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
