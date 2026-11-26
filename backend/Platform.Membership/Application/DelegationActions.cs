using System.Collections.Frozen;
using Platform.Access.Domain.Constants;

namespace Platform.Membership.Application;

/// <summary>
/// ACC-07: radnje kojima se upravlja delegacijama i radnje koje smeju da se delegiraju.
/// Delegirati se smeju samo radnje za koje Access (Tim 3) i Audit (Tim 12) već dozvoljavaju
/// asistentu sa delegacijom: AllowAssistantDelegation u CourseRolePolicies i audit.events.read.
/// Sve ostalo se odbija, pa nastavnik ne može da preda npr. upis studenata.
/// </summary>
public static class DelegationActions
{
    public const string Grant = "course.delegation.grant";
    public const string Revoke = "course.delegation.revoke";
    public const string Read = "course.delegation.read";
    public const string CourseResourceType = "course";

    /// <summary>Najduže trajanje jedne delegacije (odluka tima).</summary>
    public static readonly TimeSpan MaxDuration = TimeSpan.FromDays(90);

    public static IReadOnlySet<string> Delegable { get; } = new[]
    {
        AccessActions.LabRead.Id,
        AccessActions.AttemptReadCourse.Id,
        AccessActions.AttemptReview.Id,
        AccessActions.AuditEventsRead.Id
    }.ToFrozenSet(StringComparer.Ordinal);

    public static bool IsDelegable(string? action) =>
        action is not null && Delegable.Contains(action);
}
