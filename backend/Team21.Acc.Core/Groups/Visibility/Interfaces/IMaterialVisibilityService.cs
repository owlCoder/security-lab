using Team21.Acc.Core.Common;

namespace Team21.Acc.Core.Groups.Visibility.Interfaces;

/// <summary>
/// Decides which materials the current caller may see (ACC-06). Every denial is a NotFound error.
/// </summary>
public interface IMaterialVisibilityService
{
    /// <summary>Whether the caller may do the action on the material. Null is an unknown material.</summary>
    Task<Result> CanAccessAsync(MaterialRef? material, MaterialAction action, CancellationToken ct = default);

    /// <summary>Materials of a course the caller may see.</summary>
    Task<Result<IReadOnlyList<MaterialRef>>> FilterVisibleAsync(Guid courseId, CancellationToken ct = default);
}
