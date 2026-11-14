namespace Team21.Acc.Core.Groups.Visibility;

/// <summary>Why a caller was denied a material or a course's material list.</summary>
public enum DenialReason
{
    UnknownAction = 1,
    AccountNotActive = 2,
    NoRule = 3,
    NotCourseMember = 4,
    NotInGroup = 5,
    UnknownVisibility = 6,
    UnknownCourse = 7,
    CourseArchived = 8
}

/// <summary>
/// Audit record for one denial. Material is null when a course's material list was denied,
/// CourseId is null when the material has no rule. Holds no material content.
/// </summary>
public sealed record VisibilityDenial(
    Guid? ActorId,
    Guid? CourseId,
    MaterialRef? Material,
    MaterialAction? Action,
    DateTime OccurredAt,
    DenialReason Reason,
    string CorrelationId);
