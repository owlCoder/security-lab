using Platform.Access.Domain.Enums;

namespace Platform.Access.Domain.Models;

public sealed record CourseMembership(
    Guid AccountId,
    Guid CourseId,
    CourseRole Role,
    MembershipStatus Status,
    IReadOnlySet<string> DelegatedActions);
